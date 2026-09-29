#include <assert.h>
#include <stdbool.h>
#include <string.h>

#include <SDL3/SDL.h>

#include "ipc/machine_exit.h"

/** Check terminal-reason decisions against a real SDL quit observation. */
int
main(void) {
    assert(SDL_Init(SDL_INIT_EVENTS));

    sc_machine_exit_reset();
    SDL_Event quit = {.type = SDL_EVENT_QUIT};
    assert(SDL_PushEvent(&quit));
    SDL_Event observed;
    assert(SDL_WaitEvent(&observed));
    sc_machine_exit_observe_event(observed.type, false);
    assert(!strcmp(sc_machine_exit_reason(true, false, "unknown"),
                   "windowClosed"));

    sc_machine_exit_reset();
    sc_machine_exit_observe_event(SDL_EVENT_QUIT, true);
    sc_machine_exit_observe_event(SDL_EVENT_QUIT, true);
    assert(!strcmp(sc_machine_exit_reason(true, true, "userStop"),
                   "userStop"));
    assert(!strcmp(sc_machine_exit_reason(true, true, "protocolError"),
                   "protocolError"));

    sc_machine_exit_reset();
    sc_machine_exit_observe_event(SDL_EVENT_QUIT, false);
    assert(!strcmp(sc_machine_exit_reason(false, false, "unknown"),
                   "nativeFailure"));

    sc_machine_exit_reset();
    sc_machine_exit_observe_event(SDL_EVENT_QUIT, true);
    assert(!strcmp(sc_machine_exit_reason(false, true, "userStop"),
                   "nativeFailure"));

    sc_machine_exit_reset();
    sc_machine_exit_observe_event(SDL_EVENT_USER, false);
    assert(!strcmp(sc_machine_exit_reason(true, false, "unknown"),
                   "unknown"));
    SDL_Quit();
    return 0;
}
