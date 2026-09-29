#ifndef SC_IPC_MACHINE_H
#define SC_IPC_MACHINE_H

#include <stdbool.h>
#include <stdint.h>
#include <SDL3/SDL_video.h>

/** Validated Desktop process metadata supplied only to explicit machine mode. */
struct sc_machine_config {
    const char *session_id;
    uint32_t parent_pid;
    uint64_t parent_created;
};

/** Reserve stdout for frames before any banner, log or CLI parser runs. */
bool sc_machine_prepare_stdio(void);

/** Prepare the binary channel, ordered writer and exact parent guard. */
bool sc_machine_init(const struct sc_machine_config *config);

/** Read exactly one hello and queue the first response before device work. */
bool sc_machine_handshake(void);

/** Start the command reader after successful negotiation; returns immediately. */
bool sc_machine_start(void (*request_stop)(void *), void *userdata);

/** True only for an initialized, negotiated machine connection. */
bool sc_machine_is_active(void);

/** Bind the next real transport attempt before its native worker starts. */
void sc_machine_set_attempt_id(const char attempt_id[37], bool reconnecting);

/** Publish only the first frame successfully presented for this attempt. */
void sc_machine_on_frame_presented(void);

/** Set only on the SDL main thread; NULL before destroying the window. */
void sc_machine_set_window(SDL_Window *window);

/** Publish an observation through the single ordered writer. */
bool sc_machine_emit_lifecycle(const char *event_type, const char *subsystem,
                               const char *attempt_id, const char *reason,
                               const char *error);

/** Produce a canonical nonzero GUID for a concrete transport attempt. */
bool sc_machine_new_attempt_id(char attempt_id[37]);

/** The command reader or channel failure has requested shutdown. */
bool sc_machine_stop_requested(void);

/** Typed reason for the sticky shutdown request, without guessing source. */
const char *sc_machine_stop_reason(void);

/** Stop accepting commands and settle their main-thread completions. */
void sc_machine_close_commands(void);

/** Join all channel workers and release process handles after the runtime stops. */
void sc_machine_destroy(void);

#endif
