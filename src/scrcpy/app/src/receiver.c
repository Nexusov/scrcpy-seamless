#include "receiver.h"

#include <assert.h>
#include <inttypes.h>
#include <stdlib.h>
#include <SDL3/SDL_clipboard.h>

#include "device_msg.h"
#include "events.h"
#include "util/log.h"
#include "util/str.h"
#include "util/thread.h"

struct sc_receiver_task_data {
    struct sc_device_msg message;
};

enum {
    SC_RECEIVER_CLIPBOARD_HEADER_BYTES = 5,
    SC_RECEIVER_CLIPBOARD_TERMINATOR_BYTES = 1,
};

bool
sc_receiver_init(struct sc_receiver *receiver, sc_socket control_socket,
                 const struct sc_receiver_callbacks *cbs, void *cbs_userdata) {
    bool ok = sc_mutex_init(&receiver->mutex);
    if (!ok) {
        return false;
    }

    receiver->control_socket = control_socket;
    receiver->acksync = NULL;
    receiver->uhid_devices = NULL;
    receiver->dispatcher = NULL;
    receiver->generation = 0;
    receiver->dispatch_rejection_reported = false;

    assert(cbs && cbs->on_ended);
    receiver->cbs = cbs;
    receiver->cbs_userdata = cbs_userdata;

    return true;
}

void
sc_receiver_destroy(struct sc_receiver *receiver) {
    sc_mutex_destroy(&receiver->mutex);
}

/** Execute the clipboard effect only after the dispatcher admits its generation. */
static void
task_set_clipboard(const char *text) {
    assert(sc_thread_is_main());

#ifdef SC_TEST
    sc_receiver_test_set_clipboard(text);
#else
    char *current = SDL_GetClipboardText();
    bool same = current && !strcmp(current, text);
    SDL_free(current);
    if (same) {
        LOGD("Computer clipboard unchanged");
    } else {
        bool ok = SDL_SetClipboardText(text);
        if (ok) {
            LOGI("Device clipboard copied");
        } else {
            LOGE("Could not set clipboard: %s", SDL_GetError());
        }
    }

#endif
}

/** Resolve the validated binding for this callback, never from a queued pointer. */
static void
task_run(void *binding, void *userdata) {
    assert(sc_thread_is_main());

    struct sc_receiver_task_data *data = userdata;
    struct sc_device_msg *message = &data->message;

    if (message->type == DEVICE_MSG_TYPE_CLIPBOARD) {
        task_set_clipboard(message->clipboard.text);
        return;
    }

    assert(message->type == DEVICE_MSG_TYPE_UHID_OUTPUT);
    assert(binding);
    sc_uhid_devices_process_hid_output(binding, message->uhid_output.id,
                                       message->uhid_output.data,
                                       message->uhid_output.size);
}

/** Release owned data without accessing a generation-owned destination. */
static void
task_destroy(void *userdata) {
    struct sc_receiver_task_data *data = userdata;
    sc_device_msg_destroy(&data->message);
    free(data);
}

/** Transfer payload ownership once, or destroy it under the producer on rejection. */
static void
post_msg(struct sc_receiver *receiver, struct sc_device_msg *message,
          size_t message_size) {
#ifdef SC_TEST
    struct sc_receiver_task_data *data =
        sc_receiver_test_allocate_task(sizeof(*data));
#else
    struct sc_receiver_task_data *data = malloc(sizeof(*data));
#endif

    if (!data) {
        LOG_OOM();
        sc_device_msg_destroy(message);
        return;
    }

    data->message = *message;
    size_t payload_size = message->type == DEVICE_MSG_TYPE_CLIPBOARD
        ? message_size - SC_RECEIVER_CLIPBOARD_HEADER_BYTES
            + SC_RECEIVER_CLIPBOARD_TERMINATOR_BYTES
        : message->uhid_output.size;
    struct sc_dispatcher_task task = {
        .run = task_run,
        .destroy = task_destroy,
        .payload = data,
        .payload_bytes = sizeof(*data) + payload_size,
    };
    enum sc_dispatcher_admission admission = receiver->dispatcher
        ? sc_dispatcher_post(receiver->dispatcher, receiver->generation,
                             &task, NULL)
        : SC_DISPATCHER_REJECTED_CLOSED;

    if (admission == SC_DISPATCHER_ACCEPTED) {
        // The callback can already have retired data; never inspect it here.
        return;
    }

    task_destroy(data);

    if (!receiver->dispatch_rejection_reported) {
        receiver->dispatch_rejection_reported = true;
        LOGW("Receiver work rejected for generation %" PRIu64_ " (reason=%d)",
             receiver->generation, (int) admission);
    }
}

static void
process_msg(struct sc_receiver *receiver, struct sc_device_msg *msg,
             size_t message_size) {
    switch (msg->type) {
        case DEVICE_MSG_TYPE_CLIPBOARD:
            post_msg(receiver, msg, message_size);
            break;
        case DEVICE_MSG_TYPE_ACK_CLIPBOARD:
            LOGD("Ack device clipboard sequence=%" PRIu64_,
                 msg->ack_clipboard.sequence);

            // This is a programming error to receive this message if there is
            // no ACK synchronization mechanism
            assert(receiver->acksync);

            // Also check at runtime (do not trust the server)
            if (!receiver->acksync) {
                LOGE("Received unexpected ack");
                return;
            }

            sc_acksync_ack(receiver->acksync, msg->ack_clipboard.sequence);
            // No allocation to free in the msg
            break;
        case DEVICE_MSG_TYPE_UHID_OUTPUT:
            if (sc_get_log_level() <= SC_LOG_LEVEL_VERBOSE) {
                char *hex = sc_str_to_hex_string(msg->uhid_output.data,
                                                 msg->uhid_output.size);
                if (hex) {
                    LOGV("UHID output [%" PRIu16 "] %s",
                         msg->uhid_output.id, hex);
                    free(hex);
                } else {
                    LOGV("UHID output [%" PRIu16 "] size=%" PRIu16,
                         msg->uhid_output.id, msg->uhid_output.size);
                }
            }

            if (!receiver->uhid_devices) {
                LOGE("Received unexpected HID output message");
                sc_device_msg_destroy(msg);
                return;
            }

            post_msg(receiver, msg, message_size);
            break;
    }
}

static ssize_t
process_msgs(struct sc_receiver *receiver, const uint8_t *buf, size_t len) {
    size_t head = 0;
    for (;;) {
        struct sc_device_msg msg;
        ssize_t r = sc_device_msg_deserialize(&buf[head], len - head, &msg);
        if (r == -1) {
            return -1;
        }
        if (r == 0) {
            return head;
        }

        process_msg(receiver, &msg, (size_t) r);
        // the device msg must be destroyed by process_msg()

        head += r;
        assert(head <= len);
        if (head == len) {
            return head;
        }
    }
}

#ifdef SC_TEST
/** Expose the production decode-to-owner boundary to deterministic tests. */
ssize_t
sc_receiver_test_process_messages(struct sc_receiver *receiver,
                                   const uint8_t *buffer, size_t length) {
    return process_msgs(receiver, buffer, length);
}
#endif

static int
run_receiver(void *data) {
    struct sc_receiver *receiver = data;

    static uint8_t buf[DEVICE_MSG_MAX_SIZE];
    size_t head = 0;

    bool error = false;

    for (;;) {
        assert(head < DEVICE_MSG_MAX_SIZE);
        ssize_t r = net_recv(receiver->control_socket, buf + head,
                             DEVICE_MSG_MAX_SIZE - head);
        if (r <= 0) {
            LOGD("Receiver stopped");
            // device disconnected: keep error=false
            break;
        }

        head += r;
        ssize_t consumed = process_msgs(receiver, buf, head);
        if (consumed == -1) {
            // an error occurred
            error = true;
            break;
        }

        if (consumed) {
            head -= consumed;
            // shift the remaining data in the buffer
            memmove(buf, &buf[consumed], head);
        }
    }

    receiver->cbs->on_ended(receiver, error, receiver->cbs_userdata);

    return 0;
}

bool
sc_receiver_start(struct sc_receiver *receiver) {
    LOGD("Starting receiver thread");

    bool ok = sc_thread_create(&receiver->thread, run_receiver,
                               "scrcpy-receiver", receiver);
    if (!ok) {
        LOGE("Could not start receiver thread");
        return false;
    }

    return true;
}

void
sc_receiver_join(struct sc_receiver *receiver) {
    sc_thread_join(&receiver->thread, NULL);
}
