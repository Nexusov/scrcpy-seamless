#ifndef SC_IPC_MACHINE_EXIT_H
#define SC_IPC_MACHINE_EXIT_H

#include <stdbool.h>
#include <stdint.h>

/** Track only an observed user quit in the opt-in machine session. */
void sc_machine_exit_reset(void);
void sc_machine_exit_observe_event(uint32_t event_type, bool stop_requested);

/** Select the terminal reason without inferring a user close from exit zero. */
const char *sc_machine_exit_reason(bool success, bool stop_requested,
                                   const char *stop_reason);

#endif
