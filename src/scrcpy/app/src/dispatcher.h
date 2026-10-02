#ifndef SC_DISPATCHER_H
#define SC_DISPATCHER_H

#include "common.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <SDL3/SDL_events.h>

#include "util/thread.h"

#define SC_DISPATCHER_MAX_ITEMS 64
#define SC_DISPATCHER_MAX_PAYLOAD_BYTES (1024 * 1024)
#define SC_DISPATCHER_DRAIN_LIMIT 8

typedef uint64_t sc_dispatcher_generation;

enum sc_dispatcher_admission {
    SC_DISPATCHER_ACCEPTED,
    SC_DISPATCHER_REJECTED_CLOSED,
    SC_DISPATCHER_REJECTED_GENERATION,
    SC_DISPATCHER_REJECTED_COUNT,
    SC_DISPATCHER_REJECTED_BYTES,
    SC_DISPATCHER_REJECTED_INVALID,
};

enum sc_dispatcher_result {
    SC_DISPATCHER_PENDING,
    SC_DISPATCHER_EXECUTED,
    SC_DISPATCHER_REVOKED,
    SC_DISPATCHER_CANCELLED,
    SC_DISPATCHER_SHUTDOWN,
    SC_DISPATCHER_WAKE_FAILED,
    SC_DISPATCHER_MAIN_THREAD_REJECTED,
};

struct sc_dispatcher_task {
    void (*run)(void *binding, void *payload);
    void (*destroy)(void *payload);
    void *payload;
    size_t payload_bytes;
};

struct sc_dispatcher;

/* A caller reference and an operation reference keep this fixed slot alive. */
struct sc_dispatcher_completion {
    struct sc_dispatcher *owner;
    size_t index;
    sc_mutex mutex;
    sc_cond condition;
    enum sc_dispatcher_result caller_result;
    enum sc_dispatcher_result execution_result;
    bool caller_owned; // admission mutex
};

enum sc_dispatcher_item_state {
    SC_DISPATCHER_ITEM_FREE,
    SC_DISPATCHER_ITEM_QUEUED,
    SC_DISPATCHER_ITEM_EXECUTING,
    SC_DISPATCHER_ITEM_RETIRING,
    SC_DISPATCHER_ITEM_RETIRED,
};

struct sc_dispatcher_item {
    struct sc_dispatcher_completion completion;
    struct sc_dispatcher_task task;
    sc_dispatcher_generation generation;
    enum sc_dispatcher_item_state state;
};

typedef bool (*sc_dispatcher_wakeup_fn)(void *userdata, SDL_Event *event);

struct sc_dispatcher_stats {
    size_t retained_count;
    size_t retained_payload_bytes;
    size_t queued_count;
    size_t publishers;
};

/* Main owns the instance; final destruction requires quiesced callers/producers. */
struct sc_dispatcher {
    sc_mutex mutex;
    struct sc_dispatcher_item items[SC_DISPATCHER_MAX_ITEMS];
    size_t queue[SC_DISPATCHER_MAX_ITEMS];
    size_t head;
    struct sc_dispatcher_stats stats;
    sc_dispatcher_generation last_generation;
    sc_dispatcher_generation generation;
    void *binding;
    uintptr_t wake_ticket;
    uint32_t wake_event_type;
    sc_dispatcher_wakeup_fn wakeup;
    void *wakeup_userdata;
    bool closed;
    bool wake_pending;
    bool draining; // main thread only: the non-reentrant execution lease
};

bool
sc_dispatcher_init(struct sc_dispatcher *dispatcher, uint32_t wake_event_type,
                   sc_dispatcher_wakeup_fn wakeup, void *wakeup_userdata);

/* These main-thread transitions reject reentry while a drain is active. */
bool
sc_dispatcher_generation_begin(struct sc_dispatcher *dispatcher, void *binding,
                               sc_dispatcher_generation *generation);

bool
sc_dispatcher_generation_bind(struct sc_dispatcher *dispatcher,
                              sc_dispatcher_generation generation,
                              void *binding);

bool
sc_dispatcher_generation_revoke(struct sc_dispatcher *dispatcher,
                                sc_dispatcher_generation generation);

/* Only ACCEPTED transfers payload ownership, including a later failed wakeup. */
enum sc_dispatcher_admission
sc_dispatcher_post(struct sc_dispatcher *dispatcher,
                   sc_dispatcher_generation generation,
                   const struct sc_dispatcher_task *task,
                   struct sc_dispatcher_completion **completion);

/* Consume only this instance's event, with bounded progress per turn. */
bool
sc_dispatcher_handle_event(struct sc_dispatcher *dispatcher,
                           const SDL_Event *event);

size_t
sc_dispatcher_drain(struct sc_dispatcher *dispatcher);

/* Close admission and settle queued work before joining producers. */
bool
sc_dispatcher_shutdown(struct sc_dispatcher *dispatcher);

/* After shutdown/join/reference release, remove owned wakeups and free sync. */
bool
sc_dispatcher_destroy(struct sc_dispatcher *dispatcher);

/* Worker-only wait; cancellation does not retire executing callback data. */
enum sc_dispatcher_result
sc_dispatcher_completion_wait(struct sc_dispatcher_completion *completion);

void
sc_dispatcher_completion_cancel(struct sc_dispatcher_completion *completion);

void
sc_dispatcher_completion_release(struct sc_dispatcher_completion *completion);

enum sc_dispatcher_result
sc_dispatcher_completion_execution_result(
        struct sc_dispatcher_completion *completion);

struct sc_dispatcher_stats
sc_dispatcher_get_stats(struct sc_dispatcher *dispatcher);

#endif
