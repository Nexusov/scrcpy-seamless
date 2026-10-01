#include <assert.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <SDL3/SDL.h>

#include "device_msg.h"
#include "dispatcher.h"
#include "events.h"

struct test_gate {
    sc_mutex mutex;
    sc_cond condition;
    bool open;
};

struct test_probe {
    struct sc_dispatcher *dispatcher;
    sc_dispatcher_generation generation;
    void *expected_binding;
    atomic_uint runs;
    atomic_uint destroys;
    struct test_gate *entered;
    struct test_gate *release;
    bool nested;
    size_t refill;
};

struct test_payload {
    struct test_probe *probe;
};

struct test_wakeup {
    struct sc_dispatcher *dispatcher;
    struct test_gate *entered;
    struct test_gate *release;
    atomic_uint calls;
    unsigned fail_call;
    unsigned inline_call;
    bool last_drain_post;
    struct test_probe *post_probe;
};

struct test_producer {
    struct sc_dispatcher *dispatcher;
    sc_dispatcher_generation generation;
    struct sc_dispatcher_task task;
    struct sc_dispatcher_completion *completion;
    enum sc_dispatcher_admission admission;
    enum sc_dispatcher_result result;
    struct test_gate posted;
    bool wait;
};

/** Initialize a deterministic synchronization gate. */
static void
test_gate_init(struct test_gate *gate) {
    gate->open = false;
    assert(sc_mutex_init(&gate->mutex));
    assert(sc_cond_init(&gate->condition));
}

/** Signal a gate without a timing assumption. */
static void
test_gate_open(struct test_gate *gate) {
    sc_mutex_lock(&gate->mutex);
    gate->open = true;
    sc_cond_broadcast(&gate->condition);
    sc_mutex_unlock(&gate->mutex);
}

/** Wait only on a test-local gate, never a dispatcher lock. */
static void
test_gate_wait(struct test_gate *gate) {
    sc_mutex_lock(&gate->mutex);
    while (!gate->open) {
        sc_cond_wait(&gate->condition, &gate->mutex);
    }
    sc_mutex_unlock(&gate->mutex);
}

/** Destroy a gate after its participating worker has joined. */
static void
test_gate_destroy(struct test_gate *gate) {
    sc_cond_destroy(&gate->condition);
    sc_mutex_destroy(&gate->mutex);
}

static struct sc_dispatcher_task
test_task(struct test_probe *probe, size_t bytes);

/** Observe execution, optional nested pumping, and an explicitly held lease. */
static void
test_run(void *binding, void *userdata) {
    struct test_payload *payload = userdata;
    struct test_probe *probe = payload->probe;
    assert(sc_thread_is_main());
    assert(!sc_mutex_held(&probe->dispatcher->mutex));
    assert(binding == probe->expected_binding);
    atomic_fetch_add(&probe->runs, 1);

    if (probe->nested) {
        assert(!sc_dispatcher_generation_bind(probe->dispatcher,
                                              probe->generation, NULL));
        assert(!sc_dispatcher_generation_revoke(probe->dispatcher,
                                                probe->generation));
        sc_dispatcher_generation replacement;
        assert(!sc_dispatcher_generation_begin(probe->dispatcher, NULL,
                                               &replacement));
        assert(!sc_dispatcher_shutdown(probe->dispatcher));
        assert(!sc_dispatcher_destroy(probe->dispatcher));
        assert(!sc_dispatcher_drain(probe->dispatcher));
    }

    if (probe->refill) {
        --probe->refill;
        struct sc_dispatcher_task task = test_task(probe, sizeof(*payload));
        assert(sc_dispatcher_post(probe->dispatcher, probe->generation, &task,
                                   NULL) == SC_DISPATCHER_ACCEPTED);
    }

    if (probe->entered) {
        test_gate_open(probe->entered);
        test_gate_wait(probe->release);
    }
}

/** Release owned bytes once, checking that effects never run under the gate. */
static void
test_destroy_payload(void *userdata) {
    struct test_payload *payload = userdata;
    struct test_probe *probe = payload->probe;
    assert(!sc_mutex_held(&probe->dispatcher->mutex));
    atomic_fetch_add(&probe->destroys, 1);
    free(payload);
}

/** Allocate the exact accounted bytes, including a small ownership record. */
static struct sc_dispatcher_task
test_task(struct test_probe *probe, size_t bytes) {
    assert(bytes >= sizeof(struct test_payload));
    struct test_payload *payload = malloc(bytes);
    assert(payload);
    payload->probe = probe;
    return (struct sc_dispatcher_task) {
        .run = test_run,
        .destroy = test_destroy_payload,
        .payload = payload,
        .payload_bytes = bytes,
    };
}

/** Initialize ownership observations without global test state. */
static void
test_probe_init(struct test_probe *probe, struct sc_dispatcher *dispatcher,
                 sc_dispatcher_generation generation, void *binding) {
    *probe = (struct test_probe) {
        .dispatcher = dispatcher,
        .generation = generation,
        .expected_binding = binding,
    };
    atomic_init(&probe->runs, 0);
    atomic_init(&probe->destroys, 0);
}

/** Schedule through SDL or hold/fail a precisely chosen publication. */
static bool
test_wake(void *userdata, SDL_Event *event) {
    struct test_wakeup *wake = userdata;
    assert(!sc_mutex_held(&wake->dispatcher->mutex));
    unsigned call = atomic_fetch_add(&wake->calls, 1) + 1;

    if (wake->entered && call == 1) {
        test_gate_open(wake->entered);
        test_gate_wait(wake->release);
    }

    if (wake->last_drain_post && call == 2) {
        // The outer finite drain has ended before this external callback.
        struct sc_dispatcher_task task =
            test_task(wake->post_probe, sizeof(struct test_payload));
        assert(sc_dispatcher_post(wake->dispatcher,
                                   wake->post_probe->generation, &task,
                                   NULL) == SC_DISPATCHER_ACCEPTED);
    }

    if (call == wake->inline_call) {
        assert(sc_dispatcher_handle_event(wake->dispatcher, event));
        return true;
    }

    return call != wake->fail_call && SDL_PushEvent(event);
}

/** Construct an independent production dispatcher and fresh generation. */
static sc_dispatcher_generation
test_init(struct sc_dispatcher *dispatcher, struct test_wakeup *wake,
          void *binding) {

    if (wake) {
        wake->dispatcher = dispatcher;
        atomic_init(&wake->calls, 0);
    }

    assert(sc_dispatcher_init(dispatcher, SC_EVENT_DISPATCHER_WAKEUP,
                              wake ? test_wake : NULL, wake));
    sc_dispatcher_generation generation;
    assert(sc_dispatcher_generation_begin(dispatcher, binding, &generation));
    return generation;
}

/** Settle and destroy an owner without touching unrelated SDL events. */
static void
test_finish(struct sc_dispatcher *dispatcher) {
    assert(sc_dispatcher_shutdown(dispatcher));
    assert(sc_dispatcher_destroy(dispatcher));
}

/** Deliver only explicit queued dispatcher wakeups through the production API. */
static size_t
test_deliver_all(struct sc_dispatcher *dispatcher) {
    size_t delivered = 0;
    SDL_Event event;
    while (SDL_PeepEvents(&event, 1, SDL_GETEVENT, SC_EVENT_DISPATCHER_WAKEUP,
                          SC_EVENT_DISPATCHER_WAKEUP) == 1) {
        assert(sc_dispatcher_handle_event(dispatcher, &event));
        ++delivered;
        assert(delivered < SC_DISPATCHER_MAX_ITEMS * 2);
    }
    return delivered;
}

/** Publish on a real worker, optionally awaiting its own completion reference. */
static int
test_produce(void *userdata) {
    struct test_producer *producer = userdata;
    producer->admission = sc_dispatcher_post(producer->dispatcher,
                                            producer->generation,
                                            &producer->task,
                                            producer->wait
                                                ? &producer->completion : NULL);
    test_gate_open(&producer->posted);

    if (producer->admission != SC_DISPATCHER_ACCEPTED) {
        producer->task.destroy(producer->task.payload);
    } else if (producer->wait) {
        producer->result = sc_dispatcher_completion_wait(producer->completion);
        sc_dispatcher_completion_release(producer->completion);
    }

    return 0;
}

/** Start one producer using heap-owned task data and a controlled publication. */
static void
test_producer_start(struct test_producer *producer, sc_thread *thread,
                     struct test_probe *probe, bool wait) {
    *producer = (struct test_producer) {
        .dispatcher = probe->dispatcher,
        .generation = probe->generation,
        .task = test_task(probe, sizeof(struct test_payload)),
        .wait = wait,
    };
    test_gate_init(&producer->posted);
    assert(sc_thread_create(thread, test_produce, "dispatch-test", producer));
}

/** Normal admission owns exactly one callback and one payload release. */
static void
test_normal_and_binding(void) {
    struct sc_dispatcher dispatcher;
    int binding;
    sc_dispatcher_generation generation = test_init(&dispatcher, NULL, NULL);
    assert(sc_dispatcher_generation_bind(&dispatcher, generation, &binding));
    struct test_probe probe;
    test_probe_init(&probe, &dispatcher, generation, &binding);
    struct sc_dispatcher_task task = test_task(&probe, sizeof(struct test_payload));
    struct sc_dispatcher_completion *completion;
    assert(sc_dispatcher_post(&dispatcher, generation, &task, &completion)
            == SC_DISPATCHER_ACCEPTED);
    assert(sc_dispatcher_completion_wait(completion)
            == SC_DISPATCHER_MAIN_THREAD_REJECTED);
    assert(test_deliver_all(&dispatcher));
    assert(atomic_load(&probe.runs) == 1);
    assert(atomic_load(&probe.destroys) == 1);
    assert(sc_dispatcher_completion_execution_result(completion)
            == SC_DISPATCHER_EXECUTED);
    assert(sc_dispatcher_get_stats(&dispatcher).retained_count == 1);
    assert(!sc_dispatcher_get_stats(&dispatcher).retained_payload_bytes);
    assert(sc_dispatcher_shutdown(&dispatcher));
    assert(!sc_dispatcher_destroy(&dispatcher));
    sc_dispatcher_completion_release(completion);
    assert(sc_dispatcher_destroy(&dispatcher));
}

/** Rejection leaves payload with its producer and never calls its destructor. */
static void
test_rejected_ownership(void) {
    struct sc_dispatcher dispatcher;
    sc_dispatcher_generation generation = test_init(&dispatcher, NULL, NULL);
    struct test_probe probe;
    test_probe_init(&probe, &dispatcher, generation, NULL);
    struct sc_dispatcher_task task = test_task(&probe, sizeof(struct test_payload));
    assert(sc_dispatcher_generation_revoke(&dispatcher, generation));
    assert(sc_dispatcher_post(&dispatcher, generation, &task, NULL)
            == SC_DISPATCHER_REJECTED_GENERATION);
    assert(!atomic_load(&probe.destroys));
    assert(sc_dispatcher_shutdown(&dispatcher));
    assert(sc_dispatcher_post(&dispatcher, generation, &task, NULL)
            == SC_DISPATCHER_REJECTED_CLOSED);
    task.destroy(task.payload);
    assert(atomic_load(&probe.destroys) == 1);
    assert(sc_dispatcher_destroy(&dispatcher));
}

/** Count budget includes queued items and retained completion references. */
static void
test_count_pressure(void) {
    struct sc_dispatcher dispatcher;
    struct test_wakeup wake = {0};
    sc_dispatcher_generation generation = test_init(&dispatcher, &wake, NULL);
    struct test_probe probe;
    test_probe_init(&probe, &dispatcher, generation, NULL);
    struct sc_dispatcher_completion *completions[SC_DISPATCHER_MAX_ITEMS];
    for (size_t index = 0; index < SC_DISPATCHER_MAX_ITEMS; ++index) {
        struct sc_dispatcher_task task =
            test_task(&probe, sizeof(struct test_payload));
        assert(sc_dispatcher_post(&dispatcher, generation, &task,
                                   &completions[index]) == SC_DISPATCHER_ACCEPTED);
    }
    assert(atomic_load(&wake.calls) == 1);
    struct sc_dispatcher_task rejected =
        test_task(&probe, sizeof(struct test_payload));
    assert(sc_dispatcher_post(&dispatcher, generation, &rejected, NULL)
            == SC_DISPATCHER_REJECTED_COUNT);
    assert(sc_dispatcher_drain(&dispatcher) == SC_DISPATCHER_DRAIN_LIMIT);
    assert(sc_dispatcher_get_stats(&dispatcher).retained_count
            == SC_DISPATCHER_MAX_ITEMS);
    assert(sc_dispatcher_post(&dispatcher, generation, &rejected, NULL)
            == SC_DISPATCHER_REJECTED_COUNT);
    test_deliver_all(&dispatcher);
    assert(atomic_load(&probe.runs) == SC_DISPATCHER_MAX_ITEMS);
    assert(!sc_dispatcher_get_stats(&dispatcher).retained_payload_bytes);
    for (size_t index = 0; index < SC_DISPATCHER_MAX_ITEMS; ++index) {
        sc_dispatcher_completion_release(completions[index]);
    }
    assert(sc_dispatcher_post(&dispatcher, generation, &rejected, NULL)
            == SC_DISPATCHER_ACCEPTED);
    test_deliver_all(&dispatcher);
    assert(atomic_load(&probe.destroys) == SC_DISPATCHER_MAX_ITEMS + 1);
    test_finish(&dispatcher);
}

/** Byte accounting admits a maximum clipboard and rejects real excess memory. */
static void
test_byte_pressure(void) {
    struct sc_dispatcher dispatcher;
    sc_dispatcher_generation generation = test_init(&dispatcher, NULL, NULL);
    struct test_probe probe;
    test_probe_init(&probe, &dispatcher, generation, NULL);
    const size_t maximum_clipboard =
        DEVICE_MSG_TEXT_MAX_LENGTH + 1 + sizeof(struct test_payload);
    assert(maximum_clipboard < SC_DISPATCHER_MAX_PAYLOAD_BYTES);
    size_t total = 0;
    while (maximum_clipboard <= SC_DISPATCHER_MAX_PAYLOAD_BYTES - total) {
        struct sc_dispatcher_task task = test_task(&probe, maximum_clipboard);
        assert(sc_dispatcher_post(&dispatcher, generation, &task, NULL)
                == SC_DISPATCHER_ACCEPTED);
        total += maximum_clipboard;
    }
    assert(sc_dispatcher_get_stats(&dispatcher).retained_payload_bytes == total);
    struct sc_dispatcher_task rejected = test_task(&probe, maximum_clipboard);
    assert(sc_dispatcher_post(&dispatcher, generation, &rejected, NULL)
            == SC_DISPATCHER_REJECTED_BYTES);
    rejected.destroy(rejected.payload);
    test_deliver_all(&dispatcher);
    assert(!sc_dispatcher_get_stats(&dispatcher).retained_payload_bytes);
    struct sc_dispatcher_task exact =
        test_task(&probe, SC_DISPATCHER_MAX_PAYLOAD_BYTES);
    assert(sc_dispatcher_post(&dispatcher, generation, &exact, NULL)
            == SC_DISPATCHER_ACCEPTED);
    rejected = test_task(&probe, sizeof(struct test_payload));
    assert(sc_dispatcher_post(&dispatcher, generation, &rejected, NULL)
            == SC_DISPATCHER_REJECTED_BYTES);
    rejected.destroy(rejected.payload);
    assert(sc_dispatcher_generation_revoke(&dispatcher, generation));
    assert(!sc_dispatcher_get_stats(&dispatcher).retained_payload_bytes);
    test_finish(&dispatcher);
}

/** Exercise worker waiters with execution, revoke and final-shutdown settlement. */
static void
test_waiter_outcomes(void) {
    const enum sc_dispatcher_result expected[] = {
        SC_DISPATCHER_EXECUTED, SC_DISPATCHER_REVOKED, SC_DISPATCHER_SHUTDOWN,
    };
    for (size_t index = 0; index < ARRAY_LEN(expected); ++index) {
        struct sc_dispatcher dispatcher;
        sc_dispatcher_generation generation = test_init(&dispatcher, NULL, NULL);
        struct test_probe probe;
        test_probe_init(&probe, &dispatcher, generation, NULL);
        struct test_producer producer;
        sc_thread thread;
        test_producer_start(&producer, &thread, &probe, true);
        test_gate_wait(&producer.posted);

        if (expected[index] == SC_DISPATCHER_EXECUTED) {
            test_deliver_all(&dispatcher);
        } else if (expected[index] == SC_DISPATCHER_REVOKED) {
            assert(sc_dispatcher_generation_revoke(&dispatcher, generation));
        } else {
            assert(sc_dispatcher_shutdown(&dispatcher));
        }

        sc_thread_join(&thread, NULL);
        assert(producer.result == expected[index]);
        assert(atomic_load(&probe.destroys) == 1);
        assert(atomic_load(&probe.runs)
                == (expected[index] == SC_DISPATCHER_EXECUTED ? 1u : 0u));
        test_gate_destroy(&producer.posted);
        test_finish(&dispatcher);
    }
}

/** Deliver an old pending notification only after replacement binding exists. */
static void
test_old_generation_replacement(void) {
    struct sc_dispatcher dispatcher;
    int old_binding, new_binding;
    sc_dispatcher_generation old_generation =
        test_init(&dispatcher, NULL, &old_binding);
    struct test_probe old_probe;
    test_probe_init(&old_probe, &dispatcher, old_generation, &old_binding);
    struct sc_dispatcher_task task =
        test_task(&old_probe, sizeof(struct test_payload));
    struct sc_dispatcher_completion *completion;
    assert(sc_dispatcher_post(&dispatcher, old_generation, &task, &completion)
            == SC_DISPATCHER_ACCEPTED);
    assert(sc_dispatcher_generation_revoke(&dispatcher, old_generation));
    assert(sc_dispatcher_completion_execution_result(completion)
            == SC_DISPATCHER_REVOKED);
    sc_dispatcher_generation new_generation;
    assert(sc_dispatcher_generation_begin(&dispatcher, &new_binding,
                                          &new_generation));
    assert(new_generation != old_generation);
    test_deliver_all(&dispatcher);
    assert(!atomic_load(&old_probe.runs));
    assert(atomic_load(&old_probe.destroys) == 1);
    sc_dispatcher_completion_release(completion);
    struct test_probe new_probe;
    test_probe_init(&new_probe, &dispatcher, new_generation, &new_binding);
    task = test_task(&new_probe, sizeof(struct test_payload));
    assert(sc_dispatcher_post(&dispatcher, new_generation, &task, NULL)
            == SC_DISPATCHER_ACCEPTED);
    test_deliver_all(&dispatcher);
    assert(atomic_load(&new_probe.runs) == 1);
    test_finish(&dispatcher);
}

/** Hold publication while revocation and replacement occur on the main thread. */
static void
test_enqueue_revoke_orderings(void) {
    for (unsigned revoke_first = 0; revoke_first < 2; ++revoke_first) {
        struct test_gate entered, release;
        test_gate_init(&entered);
        test_gate_init(&release);
        struct sc_dispatcher dispatcher;
        struct test_wakeup wake = {.entered = &entered, .release = &release};
        sc_dispatcher_generation generation = test_init(&dispatcher, &wake, NULL);
        struct test_probe probe;
        test_probe_init(&probe, &dispatcher, generation, NULL);

        if (revoke_first) {
            assert(sc_dispatcher_generation_revoke(&dispatcher, generation));
        }

        struct test_producer producer;
        sc_thread thread;
        test_producer_start(&producer, &thread, &probe, false);

        if (!revoke_first) {
            test_gate_wait(&entered);
            assert(sc_dispatcher_generation_revoke(&dispatcher, generation));
            sc_dispatcher_generation replacement;
            assert(sc_dispatcher_generation_begin(&dispatcher, NULL,
                                                  &replacement));
            test_gate_open(&release);
        }

        sc_thread_join(&thread, NULL);
        assert(producer.admission == (revoke_first
                ? SC_DISPATCHER_REJECTED_GENERATION : SC_DISPATCHER_ACCEPTED));
        assert(!atomic_load(&probe.runs));
        assert(atomic_load(&probe.destroys) == 1);
        test_deliver_all(&dispatcher);
        test_gate_destroy(&producer.posted);
        test_gate_destroy(&entered);
        test_gate_destroy(&release);
        test_finish(&dispatcher);
    }
}

/** A failed shared notification settles every admitted dependant as owned work. */
static void
test_coalesced_wakeup_failure(void) {
    struct test_gate entered, release;
    test_gate_init(&entered);
    test_gate_init(&release);
    struct sc_dispatcher dispatcher;
    struct test_wakeup wake = {
        .entered = &entered, .release = &release, .fail_call = 1,
    };
    sc_dispatcher_generation generation = test_init(&dispatcher, &wake, NULL);
    struct test_probe probe;
    test_probe_init(&probe, &dispatcher, generation, NULL);
    struct test_producer producer;
    sc_thread thread;
    test_producer_start(&producer, &thread, &probe, true);
    test_gate_wait(&entered);
    struct sc_dispatcher_task task = test_task(&probe, sizeof(struct test_payload));
    struct sc_dispatcher_completion *second;
    assert(sc_dispatcher_post(&dispatcher, generation, &task, &second)
            == SC_DISPATCHER_ACCEPTED);
    assert(atomic_load(&wake.calls) == 1);
    test_gate_open(&release);
    sc_thread_join(&thread, NULL);
    assert(producer.admission == SC_DISPATCHER_ACCEPTED);
    assert(producer.result == SC_DISPATCHER_WAKE_FAILED);
    assert(sc_dispatcher_completion_execution_result(second)
            == SC_DISPATCHER_WAKE_FAILED);
    assert(!atomic_load(&probe.runs));
    assert(atomic_load(&probe.destroys) == 2);
    sc_dispatcher_completion_release(second);
    test_gate_destroy(&producer.posted);
    test_gate_destroy(&entered);
    test_gate_destroy(&release);
    test_finish(&dispatcher);
}

/** An older failed publisher cannot cancel work with a newer notification. */
static void
test_old_wakeup_failure_after_new_producer(void) {
    struct test_gate entered, release;
    test_gate_init(&entered);
    test_gate_init(&release);
    struct sc_dispatcher dispatcher;
    struct test_wakeup wake = {
        .entered = &entered, .release = &release, .fail_call = 1,
    };
    sc_dispatcher_generation generation = test_init(&dispatcher, &wake, NULL);
    struct test_probe probe;
    test_probe_init(&probe, &dispatcher, generation, NULL);
    struct test_producer producer;
    sc_thread thread;
    test_producer_start(&producer, &thread, &probe, false);
    test_gate_wait(&entered);
    assert(sc_dispatcher_drain(&dispatcher) == 1);
    struct sc_dispatcher_task task = test_task(&probe, sizeof(struct test_payload));
    assert(sc_dispatcher_post(&dispatcher, generation, &task, NULL)
            == SC_DISPATCHER_ACCEPTED);
    test_gate_open(&release);
    sc_thread_join(&thread, NULL);
    assert(sc_dispatcher_get_stats(&dispatcher).queued_count == 1);
    test_deliver_all(&dispatcher);
    assert(atomic_load(&probe.runs) == 2);
    assert(atomic_load(&probe.destroys) == 2);
    test_gate_destroy(&producer.posted);
    test_gate_destroy(&entered);
    test_gate_destroy(&release);
    test_finish(&dispatcher);
}

struct test_canceller {
    struct sc_dispatcher_completion *completion;
    struct test_probe *probe;
    size_t bytes;
    unsigned prior_destroys;
};

/** Depart during execution, retaining actual data and budget until callback exit. */
static int
test_cancel_during_execution(void *userdata) {
    struct test_canceller *caller = userdata;
    test_gate_wait(caller->probe->entered);
    sc_dispatcher_completion_cancel(caller->completion);
    assert(sc_dispatcher_completion_wait(caller->completion)
            == SC_DISPATCHER_CANCELLED);
    assert(sc_dispatcher_completion_execution_result(caller->completion)
            == SC_DISPATCHER_PENDING);
    sc_dispatcher_completion_release(caller->completion);
    assert(atomic_load(&caller->probe->destroys) == caller->prior_destroys);
    struct sc_dispatcher_stats stats =
        sc_dispatcher_get_stats(caller->probe->dispatcher);
    assert(stats.retained_count == 1);
    assert(stats.retained_payload_bytes == caller->bytes);
    struct sc_dispatcher_task rejected =
        test_task(caller->probe, sizeof(struct test_payload));
    assert(sc_dispatcher_post(caller->probe->dispatcher,
                               caller->probe->generation, &rejected, NULL)
            == SC_DISPATCHER_REJECTED_BYTES);
    rejected.destroy(rejected.payload);
    test_gate_open(caller->probe->release);
    return 0;
}

/** Cancellation of queued work skips its effect; execution keeps an owner lease. */
static void
test_caller_departure(void) {
    struct sc_dispatcher dispatcher;
    sc_dispatcher_generation generation = test_init(&dispatcher, NULL, NULL);
    struct test_probe probe;
    test_probe_init(&probe, &dispatcher, generation, NULL);
    struct sc_dispatcher_task task = test_task(&probe, sizeof(struct test_payload));
    struct sc_dispatcher_completion *completion;
    assert(sc_dispatcher_post(&dispatcher, generation, &task, &completion)
            == SC_DISPATCHER_ACCEPTED);
    sc_dispatcher_completion_cancel(completion);
    assert(sc_dispatcher_get_stats(&dispatcher).retained_payload_bytes
            == sizeof(struct test_payload));
    test_deliver_all(&dispatcher);
    assert(!atomic_load(&probe.runs));
    assert(sc_dispatcher_completion_execution_result(completion)
            == SC_DISPATCHER_CANCELLED);
    sc_dispatcher_completion_release(completion);

    struct test_gate entered, release;
    test_gate_init(&entered);
    test_gate_init(&release);
    probe.entered = &entered;
    probe.release = &release;
    task = test_task(&probe, SC_DISPATCHER_MAX_PAYLOAD_BYTES);
    assert(sc_dispatcher_post(&dispatcher, generation, &task, &completion)
            == SC_DISPATCHER_ACCEPTED);
    struct test_canceller caller = {
        .completion = completion, .probe = &probe,
        .bytes = SC_DISPATCHER_MAX_PAYLOAD_BYTES,
        .prior_destroys = atomic_load(&probe.destroys),
    };
    sc_thread thread;
    assert(sc_thread_create(&thread, test_cancel_during_execution,
                            "cancel-test", &caller));
    test_deliver_all(&dispatcher);
    sc_thread_join(&thread, NULL);
    assert(atomic_load(&probe.runs) == 1);
    assert(atomic_load(&probe.destroys) == 3);
    assert(!sc_dispatcher_get_stats(&dispatcher).retained_count);
    test_gate_destroy(&entered);
    test_gate_destroy(&release);
    test_finish(&dispatcher);
}

struct test_empty_drain_producer {
    struct test_probe *probe;
    struct test_probe *next_probe;
};

/** Admit the final producer before the last active callback releases its lease. */
static int
test_post_before_empty_drain(void *userdata) {
    struct test_empty_drain_producer *producer = userdata;
    test_gate_wait(producer->probe->entered);
    struct sc_dispatcher_task task =
        test_task(producer->next_probe, sizeof(struct test_payload));
    assert(sc_dispatcher_post(producer->next_probe->dispatcher,
                               producer->next_probe->generation, &task, NULL)
            == SC_DISPATCHER_ACCEPTED);
    test_gate_open(producer->probe->release);
    return 0;
}

/** Both sides of the final empty decision deliver the producer's actual SDL wake. */
static void
test_last_empty_drain_race(void) {
    for (unsigned post_after_empty = 0; post_after_empty < 2; ++post_after_empty) {
        struct sc_dispatcher dispatcher;
        sc_dispatcher_generation generation = test_init(&dispatcher, NULL, NULL);
        struct test_probe probe, next_probe;
        test_probe_init(&probe, &dispatcher, generation, NULL);
        test_probe_init(&next_probe, &dispatcher, generation, NULL);
        struct test_gate entered, release;
        test_gate_init(&entered);
        test_gate_init(&release);
        sc_thread thread;
        struct test_empty_drain_producer before = {
            .probe = &probe, .next_probe = &next_probe,
        };

        if (!post_after_empty) {
            probe.entered = &entered;
            probe.release = &release;
            assert(sc_thread_create(&thread, test_post_before_empty_drain,
                                    "empty-test", &before));
        }

        struct sc_dispatcher_task task =
            test_task(&probe, sizeof(struct test_payload));
        assert(sc_dispatcher_post(&dispatcher, generation, &task, NULL)
                == SC_DISPATCHER_ACCEPTED);
        assert(sc_dispatcher_drain(&dispatcher) == (post_after_empty ? 1u : 2u));
        assert(!sc_dispatcher_get_stats(&dispatcher).queued_count);

        if (post_after_empty) {
            struct test_producer after;
            test_producer_start(&after, &thread, &next_probe, false);
            test_gate_wait(&after.posted);
            sc_thread_join(&thread, NULL);
            assert(after.admission == SC_DISPATCHER_ACCEPTED);
            test_gate_destroy(&after.posted);
        } else {
            sc_thread_join(&thread, NULL);
        }

        assert(test_deliver_all(&dispatcher));
        assert(atomic_load(&probe.runs) == 1);
        assert(atomic_load(&next_probe.runs) == 1);
        assert(atomic_load(&probe.destroys) == 1);
        assert(atomic_load(&next_probe.destroys) == 1);
        test_gate_destroy(&entered);
        test_gate_destroy(&release);
        test_finish(&dispatcher);
    }
}

/** Sustained publication remains finite per drain and reentry cannot retire data. */
static void
test_bounded_and_nested_drain(void) {
    struct sc_dispatcher dispatcher;
    struct test_wakeup wake = {0};
    sc_dispatcher_generation generation = test_init(&dispatcher, &wake, NULL);
    struct test_probe probe;
    test_probe_init(&probe, &dispatcher, generation, NULL);
    probe.nested = true;
    probe.refill = SC_DISPATCHER_DRAIN_LIMIT * 3;
    struct sc_dispatcher_task task = test_task(&probe, sizeof(struct test_payload));
    assert(sc_dispatcher_post(&dispatcher, generation, &task, NULL)
            == SC_DISPATCHER_ACCEPTED);
    assert(sc_dispatcher_drain(&dispatcher) == SC_DISPATCHER_DRAIN_LIMIT);
    assert(atomic_load(&probe.runs) == SC_DISPATCHER_DRAIN_LIMIT);
    assert(sc_dispatcher_get_stats(&dispatcher).queued_count == 1);
    test_deliver_all(&dispatcher);
    assert(atomic_load(&probe.runs) == SC_DISPATCHER_DRAIN_LIMIT * 3 + 1);
    assert(atomic_load(&probe.destroys) == atomic_load(&probe.runs));
    test_finish(&dispatcher);
}

/** Publication at final rearming, including inline delivery, cannot strand work. */
static void
test_final_drain_publication(void) {
    for (unsigned inline_delivery = 0; inline_delivery < 2; ++inline_delivery) {
        struct sc_dispatcher dispatcher;
        struct test_wakeup wake = {
            .last_drain_post = !inline_delivery,
            .inline_call = inline_delivery ? 2u : 0u,
        };
        sc_dispatcher_generation generation = test_init(&dispatcher, &wake, NULL);
        struct test_probe probe;
        test_probe_init(&probe, &dispatcher, generation, NULL);
        wake.post_probe = &probe;
        for (size_t index = 0; index < SC_DISPATCHER_DRAIN_LIMIT + 1; ++index) {
            struct sc_dispatcher_task task =
                test_task(&probe, sizeof(struct test_payload));
            assert(sc_dispatcher_post(&dispatcher, generation, &task, NULL)
                    == SC_DISPATCHER_ACCEPTED);
        }
        assert(sc_dispatcher_drain(&dispatcher) == SC_DISPATCHER_DRAIN_LIMIT);
        test_deliver_all(&dispatcher);
        assert(!sc_dispatcher_get_stats(&dispatcher).queued_count);
        unsigned expected = SC_DISPATCHER_DRAIN_LIMIT +
                            (inline_delivery ? 1u : 2u);
        assert(atomic_load(&probe.runs) == expected);
        assert(atomic_load(&probe.destroys) == expected);
        test_finish(&dispatcher);
    }
}

/** Retain an owner while a publisher is held and remove only its own final wake. */
static void
test_shutdown_publication_and_owned_wakeups(void) {
    struct test_gate entered, release;
    test_gate_init(&entered);
    test_gate_init(&release);
    struct sc_dispatcher dispatcher;
    struct test_wakeup wake = {.entered = &entered, .release = &release};
    sc_dispatcher_generation generation = test_init(&dispatcher, &wake, NULL);
    struct test_probe probe;
    test_probe_init(&probe, &dispatcher, generation, NULL);
    struct test_producer producer;
    sc_thread thread;
    test_producer_start(&producer, &thread, &probe, false);
    test_gate_wait(&entered);
    assert(sc_dispatcher_shutdown(&dispatcher));
    assert(!sc_dispatcher_destroy(&dispatcher));
    assert(atomic_load(&probe.destroys) == 1);
    test_gate_open(&release);
    sc_thread_join(&thread, NULL);
    struct sc_dispatcher other;
    test_init(&other, NULL, NULL);
    SDL_Event foreign = {
        .user = {.type = SC_EVENT_DISPATCHER_WAKEUP, .data1 = &other},
    };
    assert(SDL_PushEvent(&foreign));
    assert(sc_dispatcher_destroy(&dispatcher));
    SDL_Event event;
    assert(SDL_PeepEvents(&event, 1, SDL_GETEVENT, SC_EVENT_DISPATCHER_WAKEUP,
                          SC_EVENT_DISPATCHER_WAKEUP) == 1);
    assert(event.user.data1 == &other);
    assert(SDL_PeepEvents(&event, 1, SDL_GETEVENT, SC_EVENT_DISPATCHER_WAKEUP,
                          SC_EVENT_DISPATCHER_WAKEUP) == 0);
    test_gate_destroy(&producer.posted);
    test_gate_destroy(&entered);
    test_gate_destroy(&release);
    test_finish(&other);
}

/** Legacy cleanup cannot erase the app wakeup and generation values never wrap. */
static void
test_legacy_flush_and_exhaustion(void) {
    struct sc_dispatcher dispatcher;
    sc_dispatcher_generation generation = test_init(&dispatcher, NULL, NULL);
    struct test_probe probe;
    test_probe_init(&probe, &dispatcher, generation, NULL);
    struct sc_dispatcher_task task = test_task(&probe, sizeof(struct test_payload));
    assert(sc_dispatcher_post(&dispatcher, generation, &task, NULL)
            == SC_DISPATCHER_ACCEPTED);
    assert(SC_EVENT_DISPATCHER_WAKEUP > SC_EVENT_AOA_OPEN_ERROR);
    assert(sc_push_event(SC_EVENT_NEW_FRAME));
    SDL_FlushEvents(SC_EVENT_NEW_FRAME, SC_EVENT_AOA_OPEN_ERROR);
    test_deliver_all(&dispatcher);
    assert(atomic_load(&probe.runs) == 1);
    assert(sc_dispatcher_generation_revoke(&dispatcher, generation));
    dispatcher.last_generation = UINT64_MAX;
    assert(!sc_dispatcher_generation_begin(&dispatcher, NULL, &generation));
    test_finish(&dispatcher);

    generation = test_init(&dispatcher, NULL, NULL);
    test_probe_init(&probe, &dispatcher, generation, NULL);
    dispatcher.wake_ticket = UINTPTR_MAX - 1;
    struct sc_dispatcher_completion *first, *second;
    task = test_task(&probe, sizeof(struct test_payload));
    assert(sc_dispatcher_post(&dispatcher, generation, &task, &first)
            == SC_DISPATCHER_ACCEPTED);
    task = test_task(&probe, sizeof(struct test_payload));
    assert(sc_dispatcher_post(&dispatcher, generation, &task, &second)
            == SC_DISPATCHER_ACCEPTED);
    assert(!atomic_load(&probe.destroys)); // Last legitimate ticket still pending.
    test_deliver_all(&dispatcher);
    assert(atomic_load(&probe.runs) == 2);
    sc_dispatcher_completion_release(first);
    sc_dispatcher_completion_release(second);
    task = test_task(&probe, sizeof(struct test_payload));
    assert(sc_dispatcher_post(&dispatcher, generation, &task, &first)
            == SC_DISPATCHER_ACCEPTED);
    assert(sc_dispatcher_completion_execution_result(first)
            == SC_DISPATCHER_WAKE_FAILED);
    assert(atomic_load(&probe.destroys) == 3);
    sc_dispatcher_completion_release(first);
    test_finish(&dispatcher);
}

/** Run production dispatcher contracts without clipboard, devices or windows. */
int
main(void) {
    assert(SDL_Init(SDL_INIT_EVENTS));
    test_normal_and_binding();
    test_rejected_ownership();
    test_count_pressure();
    test_byte_pressure();
    test_waiter_outcomes();
    test_old_generation_replacement();
    test_enqueue_revoke_orderings();
    test_coalesced_wakeup_failure();
    test_old_wakeup_failure_after_new_producer();
    test_caller_departure();
    test_bounded_and_nested_drain();
    test_final_drain_publication();
    test_last_empty_drain_race();
    test_shutdown_publication_and_owned_wakeups();
    test_legacy_flush_and_exhaustion();
    puts("dispatcher: 15 contract groups passed (controlled subcases included)");
    SDL_Quit();
    return 0;
}
