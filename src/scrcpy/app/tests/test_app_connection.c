#include "common.h"

#include <stdatomic.h>
#include <errno.h>

int sc_owner_fixture_main(int argument_count, char **arguments);

/* Reuse the existing real-owner fixture, not a second lifecycle model. */
#define __wrap_sc_recorder_start sc_owner_recorder_start
#define __wrap_sc_demuxer_join sc_owner_demuxer_join
#define main sc_owner_fixture_main
#define __wrap_sc_server_init sc_owner_server_init
#define __wrap_sc_server_start sc_owner_server_start
#define __wrap_sc_server_join sc_owner_server_join
#define __wrap_sc_demuxer_start sc_owner_demuxer_start
#define __wrap_SDL_GetJoysticks sc_owner_get_joysticks
#define __wrap_sc_machine_is_active sc_owner_machine_is_active
#define __wrap_sc_machine_stop_requested sc_owner_machine_stop_requested
#define __wrap_sc_recorder_init sc_owner_recorder_init
#include "test_session.c"
#undef main
#undef __wrap_sc_demuxer_join
#undef __wrap_sc_recorder_start
#undef __wrap_sc_server_init
#undef __wrap_sc_server_start
#undef __wrap_sc_server_join
#undef __wrap_sc_demuxer_start
#undef __wrap_SDL_GetJoysticks
#undef __wrap_sc_machine_is_active
#undef __wrap_sc_machine_stop_requested
#undef __wrap_sc_recorder_init

#include "app.h"
#include "connection_plan.h"
#include "transport_resolver.h"

enum app_case {
    APP_INITIALIZATION_AFTER_RESOLVE_FAILURE,
    APP_SELECTED_NETWORK,
    APP_HEADLESS_REPLACEMENT,
    APP_FATAL_DECODER,
    APP_FATAL_SERVER,
    APP_RECORDING_AFTER_TRANSIENT,
    APP_DEADLINE_RESOLVING,
    APP_DEADLINE_RETIRING,
    APP_STOP_RESOLVING,
    APP_STOP_WAITING,
    APP_STARTUP_TIMEOUT,
    APP_STARTUP_DEADLINE,
    APP_RECORDING_LATE_FAILURE,
    APP_RECORDING_STOP_FAILURE,
    APP_RECORDING_HEALTHY_STOP,
    APP_RECORDING_PRE_MEDIA_STOP,
    APP_GENERATION_SOAK,
};

static enum app_case app_case;
static bool running_app;
static atomic_int_fast64_t fake_now;
static atomic_bool app_stop;
static atomic_uint queries;
static atomic_uint connected_queries;
static atomic_uint completed_resolutions;
static bool taken_resolve;
static unsigned servers;
static unsigned video_initializations;
static unsigned audio_initializations;
static unsigned gamepad_initializations;
static char first_server_serial[SC_CONNECTION_PLAN_ENDPOINT_CAPACITY];
static char recording_filename[80];
static struct sc_dispatcher *app_dispatcher;
static unsigned server_peak;
static unsigned startup_waits;
static struct sc_recorder *active_recording;
static AVCodecContext *recording_codec;
static AVBufferRef *recording_probe;
static unsigned real_trailers;


enum { APP_SOAK_REPLACEMENTS = 100, APP_SOAK_GENERATIONS = 101 };

bool __wrap_sc_recorder_start(struct sc_recorder *recorder);
void __wrap_sc_demuxer_join(struct sc_demuxer *demuxer);
bool __real_sc_recorder_start(struct sc_recorder *recorder);
void __real_sc_recorder_join(struct sc_recorder *recorder);
int __real_av_write_trailer(AVFormatContext *context);
int __wrap_av_write_trailer(AVFormatContext *context);
void __wrap_sc_recorder_join(struct sc_recorder *recorder);
bool __real_SDL_Init(SDL_InitFlags flags);
bool __real_SDL_WaitEventTimeout(SDL_Event *event, Sint32 timeout);
sc_tick __real_sc_tick_now(void);
bool __real_sc_transport_resolver_take(struct sc_transport_resolver *resolver,
                                       struct sc_transport_result *result);
bool __real_sc_connection_manager_connected(struct sc_connection_manager *manager,
                                             sc_connection_attempt attempt,
                                             sc_tick now);

bool __wrap_SDL_Init(SDL_InitFlags flags);
bool __wrap_SDL_WaitEventTimeout(SDL_Event *event, Sint32 timeout);
sc_tick __wrap_sc_tick_now(void);
bool __wrap_sc_machine_is_active(void);
bool __wrap_sc_machine_stop_requested(void);
SDL_JoystickID *__wrap_SDL_GetJoysticks(int *count);
bool __wrap_sc_server_init(struct sc_server *server,
                            const struct sc_server_params *params,
                            const struct sc_server_callbacks *callbacks,
                            void *userdata);
bool __wrap_sc_server_start(struct sc_server *server);
void __wrap_sc_server_join(struct sc_server *server);
bool __wrap_sc_demuxer_start(struct sc_demuxer *demuxer);
bool __wrap_sc_recorder_init(struct sc_recorder *recorder, const char *filename,
                              enum sc_record_format format, bool video, bool audio,
                              enum sc_orientation orientation,
                              const struct sc_recorder_callbacks *callbacks,
                              void *userdata);
bool __wrap_sc_adb_query(struct sc_intr *intr, const char *const argv[],
                          char *output, size_t capacity);
bool __wrap_sc_transport_resolver_take(struct sc_transport_resolver *resolver,
                                        struct sc_transport_result *result);
bool __wrap_sc_connection_manager_connected(struct sc_connection_manager *manager,
                                              sc_connection_attempt attempt,
                                              sc_tick now);

static bool
real_recording_case(void) {
    return app_case >= APP_RECORDING_LATE_FAILURE
        && app_case <= APP_RECORDING_PRE_MEDIA_STOP;
}

int
__wrap_av_write_trailer(AVFormatContext *context) {
    int result = __real_av_write_trailer(context);

    if (real_recording_case()) {
        assert(app_dispatcher && !app_dispatcher->generation);
        ++real_trailers;

        if (app_case == APP_RECORDING_LATE_FAILURE
                || app_case == APP_RECORDING_STOP_FAILURE) {
            return AVERROR(EIO);
        }
    }
    return result;
}

bool
__wrap_sc_recorder_start(struct sc_recorder *recorder) {

    if (!real_recording_case()) {
        return sc_owner_recorder_start(recorder);
    }
    bool started = __real_sc_recorder_start(recorder);
    assert(started);
    ++resources[RESOURCE_RECORDER].started;
    return true;
}

void
__wrap_sc_recorder_join(struct sc_recorder *recorder) {
    assert(!real_recording_case() || recorder == active_recording);
    __real_sc_recorder_join(recorder);
    record_join(RESOURCE_RECORDER);
}

void
__wrap_sc_demuxer_join(struct sc_demuxer *demuxer) {

    if (real_recording_case() && recording_codec) {
        sc_packet_source_sinks_close(&demuxer->packet_source);
        avcodec_free_context(&recording_codec);
    }
    sc_owner_demuxer_join(demuxer);
}

bool
__wrap_SDL_Init(SDL_InitFlags flags) {
    if (!running_app) {
        return __real_SDL_Init(flags);
    }

    video_initializations += !!(flags & SDL_INIT_VIDEO);
    audio_initializations += !!(flags & SDL_INIT_AUDIO);
    gamepad_initializations += !!(flags & SDL_INIT_GAMEPAD);
    return true;
}

bool
__wrap_SDL_WaitEventTimeout(SDL_Event *event, Sint32 timeout) {
    if (running_app && app_case == APP_STOP_WAITING && taken_resolve) {
        atomic_store_explicit(&app_stop, true, memory_order_release);
    }

    if (running_app && servers && (app_case == APP_STARTUP_TIMEOUT
                                    || app_case == APP_STARTUP_DEADLINE)) {
        ++startup_waits;
        sc_tick now = app_case == APP_STARTUP_DEADLINE ? SC_TICK_FROM_SEC(6)
            : SC_TICK_FROM_SEC(30) - (startup_waits == 1);
        atomic_store_explicit(&fake_now, now, memory_order_release);
    }

    return __real_SDL_WaitEventTimeout(event, timeout);
}

sc_tick
__wrap_sc_tick_now(void) {
    if (!running_app) {
        return __real_sc_tick_now();
    }

    return atomic_load_explicit(&fake_now, memory_order_acquire);
}

bool
__wrap_sc_machine_is_active(void) {
    return app_case == APP_STOP_RESOLVING || app_case == APP_STOP_WAITING
        || app_case == APP_RECORDING_STOP_FAILURE
        || app_case == APP_RECORDING_HEALTHY_STOP
        || app_case == APP_RECORDING_PRE_MEDIA_STOP;
}

bool
__wrap_sc_machine_stop_requested(void) {
    return atomic_load_explicit(&app_stop, memory_order_acquire);
}

SDL_JoystickID *
__wrap_SDL_GetJoysticks(int *count) {
    *count = 0;
    SDL_JoystickID *empty = SDL_malloc(sizeof(*empty));
    assert(empty);
    empty[0] = 0;
    return empty;
}

bool
__wrap_sc_server_init(struct sc_server *server,
                       const struct sc_server_params *params,
                       const struct sc_server_callbacks *callbacks,
                       void *userdata) {
    if (app_case == APP_GENERATION_SOAK) {
        assert(app_dispatcher->last_generation == servers + 1);

        for (unsigned resource = 0; resource < RESOURCE_COUNT; ++resource) {
            assert(resources[resource].acquired == resources[resource].destroyed);
            assert(resources[resource].started == resources[resource].joined);
        }

        struct sc_dispatcher_stats stats = sc_dispatcher_get_stats(app_dispatcher);
        assert(!stats.queued_count && !stats.retained_count);
        assert(!stats.retained_payload_bytes && !stats.publishers);
    }

    if (!servers) {
        assert(params->req_serial);
        assert(strlen(params->req_serial) < sizeof(first_server_serial));
        strcpy(first_server_serial, params->req_serial);
    }

    if (app_case == APP_RECORDING_LATE_FAILURE) {
        assert(!servers && "failed finalization must prevent replacement");
    }
    bool initialized = sc_owner_server_init(server, params, callbacks, userdata);
    unsigned live = resources[RESOURCE_SERVER].acquired
        - resources[RESOURCE_SERVER].destroyed;
    server_peak = MAX(server_peak, live);
    return initialized;
}

bool
__wrap_sc_server_start(struct sc_server *server) {
    ++servers;
    ++resources[RESOURCE_SERVER].started;

    if (app_case == APP_STARTUP_TIMEOUT || app_case == APP_STARTUP_DEADLINE) {
        // Keep the actual app in CONNECTING; no synthetic connected callback.
        return true;
    }

    if (app_case == APP_FATAL_SERVER) {
        server->failure = SC_SERVER_FAILURE_START;
        server->cbs->on_connection_failed(server, server->cbs_userdata);
        return true;
    }

    if ((app_case == APP_RECORDING_AFTER_TRANSIENT
            || app_case == APP_DEADLINE_RETIRING) && servers == 1) {
        server->failure = SC_SERVER_FAILURE_TRANSIENT_CONNECTION;
        server->cbs->on_connection_failed(server, server->cbs_userdata);
        return true;
    }

    server->cbs->on_connected(server, server->cbs_userdata);
    return true;
}

void
__wrap_sc_server_join(struct sc_server *server) {
    if (app_case == APP_DEADLINE_RETIRING) {
        atomic_store_explicit(&fake_now, SC_TICK_FROM_SEC(6), memory_order_release);
    }

    sc_owner_server_join(server);
}

bool
__wrap_sc_demuxer_start(struct sc_demuxer *demuxer) {
    bool started = sc_owner_demuxer_start(demuxer);
    assert(started);

    if (real_recording_case()) {

        if (app_case != APP_RECORDING_PRE_MEDIA_STOP) {
            const AVCodec *codec = avcodec_find_decoder(AV_CODEC_ID_PCM_S16LE);
            assert(codec);
            recording_codec = avcodec_alloc_context3(codec);
            assert(recording_codec);
            recording_codec->codec_type = AVMEDIA_TYPE_AUDIO;
            recording_codec->sample_rate = 48000;
            recording_codec->sample_fmt = AV_SAMPLE_FMT_S16;
            av_channel_layout_default(&recording_codec->ch_layout, 2);
            bool opened = sc_packet_source_sinks_open(&demuxer->packet_source,
                                                       recording_codec, NULL);
            assert(opened);
            AVPacket *packet = av_packet_alloc();
            assert(packet && !av_new_packet(packet, 16));
            memset(packet->data, 0, 16);
            packet->pts = 0;
            packet->dts = 0;
            recording_probe = av_buffer_ref(packet->buf);
            assert(recording_probe);
            bool pushed = sc_packet_source_sinks_push(&demuxer->packet_source, packet);
            assert(pushed);
            av_packet_free(&packet);
        }

        if (app_case == APP_RECORDING_LATE_FAILURE) {
            demuxer->cbs->on_ended(demuxer, SC_DEMUXER_STATUS_EOS,
                                   demuxer->cbs_userdata);
        } else {
            /* Supply explicit control-center Stop at its existing process-intent
             * boundary; no fabricated wire acknowledgement is asserted. */
            atomic_store_explicit(&app_stop, true, memory_order_release);
        }
        return true;
    }

    if (app_case == APP_FATAL_DECODER) {
        demuxer->cbs->on_ended(demuxer, SC_DEMUXER_STATUS_ERROR,
                               demuxer->cbs_userdata);
        return true;
    }

    bool replacement = app_case == APP_HEADLESS_REPLACEMENT
        || app_case == APP_INITIALIZATION_AFTER_RESOLVE_FAILURE
        || app_case == APP_GENERATION_SOAK;
    unsigned limit = app_case == APP_GENERATION_SOAK ? APP_SOAK_GENERATIONS : 2;

    if (replacement && servers < limit) {
        if (app_case == APP_INITIALIZATION_AFTER_RESOLVE_FAILURE) {
            uint8_t required = sc_session_required_capabilities(demuxer->cbs_userdata);
            fprintf(stderr, "App initialization: server=%u demux=%s required-mask=%u\n",
                    servers, demuxer->name, required);
            assert(required & SC_SESSION_CAPABILITY_AUDIO);
        }

        demuxer->cbs->on_ended(demuxer, SC_DEMUXER_STATUS_EOS,
                               demuxer->cbs_userdata);
        return true;
    }

    SDL_Event quit = {.type = SDL_EVENT_QUIT};
    bool posted = SDL_PushEvent(&quit);
    assert(posted);
    return true;
}

bool
__wrap_sc_recorder_init(struct sc_recorder *recorder, const char *filename,
                         enum sc_record_format format, bool video, bool audio,
                         enum sc_orientation orientation,
                         const struct sc_recorder_callbacks *callbacks,
                         void *userdata) {
    assert(app_case == APP_RECORDING_AFTER_TRANSIENT || real_recording_case());
    active_recording = recorder;
    assert(strlen(filename) < sizeof(recording_filename));
    strcpy(recording_filename, filename);
    // Fail only worker start, after the real recorder's queues/mutex exist.
    if (!real_recording_case()) {
        current_failure = FAIL_RECORDER_START;
    }
    return sc_owner_recorder_init(recorder, filename, format, video, audio,
                                    orientation, callbacks, userdata);
}

bool
__wrap_sc_adb_query(struct sc_intr *intr, const char *const argv[],
                     char *output, size_t capacity) {
    (void) intr;
    atomic_fetch_add_explicit(&queries, 1, memory_order_relaxed);

    if (app_case == APP_STOP_RESOLVING) {
        atomic_store_explicit(&app_stop, true, memory_order_release);
        return false;
    }

    if (app_case == APP_DEADLINE_RESOLVING) {
        atomic_store_explicit(&fake_now, SC_TICK_FROM_SEC(6), memory_order_release);
        return false;
    }

    if (app_case == APP_STOP_WAITING
            || (app_case == APP_INITIALIZATION_AFTER_RESOLVE_FAILURE
                && !atomic_load_explicit(&completed_resolutions,
                                          memory_order_acquire))) {
        return false;
    }

    const char *response;

    if (!strcmp(argv[1], "connect")) {
        assert(!strcmp(argv[2], "127.0.0.1:43210"));
        atomic_fetch_add_explicit(&connected_queries, 1, memory_order_relaxed);
        response = "connected to 127.0.0.1:43210\n";
    } else if (!strcmp(argv[1], "devices")) {
        response = "List of devices attached\n"
                   "fixture-usb device usb:1-2 model:synthetic transport_id:1\n";
    } else if (!strcmp(argv[1], "-s")) {
        assert(!strcmp(argv[2], "fixture-usb"));
        assert(!strcmp(argv[3], "get-devpath") || !strcmp(argv[3], "get-state"));
        response = !strcmp(argv[3], "get-devpath") ? "usb:1-2\n" : "device\n";
    } else {
        assert(!strcmp(argv[1], "-d") && !strcmp(argv[2], "get-serialno"));
        response = "fixture-usb\n";
    }

    assert(strlen(response) < capacity);
    strcpy(output, response);
    return true;
}

bool
__wrap_sc_transport_resolver_take(struct sc_transport_resolver *resolver,
                                   struct sc_transport_result *result) {
    bool taken = __real_sc_transport_resolver_take(resolver, result);

    if (taken) {
        taken_resolve = true;
        atomic_fetch_add_explicit(&completed_resolutions, 1, memory_order_release);
    }

    return taken;
}

bool
__wrap_sc_connection_manager_connected(struct sc_connection_manager *manager,
                                         sc_connection_attempt attempt,
                                         sc_tick now) {
    bool connected = __real_sc_connection_manager_connected(manager, attempt, now);

    if (connected && app_case == APP_GENERATION_SOAK) {
        // Advance the supplied clock only after actual policy admission.
        atomic_fetch_add_explicit(&fake_now, SC_CONNECTION_STABLE_INTERVAL,
                                    memory_order_release);
    }

    return connected;
}

static void
test_application_case(enum app_case selected_case) {
    fprintf(stderr, "App case=%u begin\n", selected_case);
    memset(resources, 0, sizeof(resources));
    current_failure = FAIL_NONE;
    app_case = selected_case;
    machine_stop = false;
    current_controller = NULL;
    graph_start_attempts = 0;
    servers = 0;
    server_peak = 0;
    startup_waits = 0;
    active_recording = NULL;
    recording_codec = NULL;
    recording_probe = NULL;
    real_trailers = 0;
    first_server_serial[0] = '\0';
    recording_filename[0] = '\0';
    video_initializations = 0;
    audio_initializations = 0;
    gamepad_initializations = 0;
    taken_resolve = false;
    atomic_init(&fake_now, 0);
    atomic_init(&app_stop, false);
    atomic_init(&queries, 0);
    atomic_init(&connected_queries, 0);
    atomic_init(&completed_resolutions, 0);
    struct sc_connection_launch_plan plan = {
        .policy = {
            .candidates = {SC_CONNECTION_USB, SC_CONNECTION_NETWORK},
            .candidate_count = 2,
            .retry_enabled = true,
            .allow_fallback = true,
            .maximum_attempts = 5,
            .maximum_backoff = SC_TICK_FROM_SEC(8),
        },
        .targets = {
            {.transport = SC_CONNECTION_USB, .serial = "fixture-usb"},
            {.transport = SC_CONNECTION_NETWORK, .endpoint = "127.0.0.1:43210"},
        },
        .reconnect = true,
    };

    if (app_case == APP_SELECTED_NETWORK) {
        plan.policy.selected_candidate = 1;
    }

    if (app_case == APP_GENERATION_SOAK) {
        plan.policy.maximum_attempts = SC_CONNECTION_PLAN_MAX_ATTEMPTS;
    }

    if (app_case == APP_STARTUP_TIMEOUT) {
        plan.policy.maximum_attempts = 1;
    }

    struct scrcpy_options options = options_for_failure(FAIL_NONE);
    options.connection_plan = &plan;
    options.video = true;

    if (app_case == APP_INITIALIZATION_AFTER_RESOLVE_FAILURE) {
        plan.required_capabilities = SC_SESSION_CAPABILITY_AUDIO
            | SC_SESSION_CAPABILITY_CONTROL;
        options.video = false;
        options.audio = true;
        options.audio_playback = true;
        options.clipboard_autosync = true;
        options.gamepad_input_mode = SC_GAMEPAD_INPUT_MODE_UHID;
    }

    if (app_case == APP_RECORDING_AFTER_TRANSIENT) {
        options.record_filename = "app-synthetic-never-opened.mkv";
        options.record_format = SC_RECORD_FORMAT_MKV;
    }

    char actual_recording_path[80];

    if (real_recording_case()) {
        snprintf(actual_recording_path, sizeof(actual_recording_path),
                 "app-recording-%lu-%u.wav", (unsigned long) GetCurrentProcessId(),
                 (unsigned) app_case);
        options.record_filename = actual_recording_path;
        options.record_format = SC_RECORD_FORMAT_WAV;
        options.video = false;
        options.audio = true;
        options.audio_playback = false;
        options.control = false;
    }

    if (app_case == APP_DEADLINE_RESOLVING || app_case == APP_DEADLINE_RETIRING
            || app_case == APP_STARTUP_DEADLINE) {
        options.time_limit = SC_TICK_FROM_SEC(5);
        plan.policy.initial_backoff = SC_TICK_FROM_SEC(1);
    }

    if (app_case == APP_STOP_WAITING) {
        plan.policy.initial_backoff = SC_TICK_FROM_SEC(1);
    }

    struct sc_dispatcher dispatcher;
    bool initialized = sc_dispatcher_init(&dispatcher, SC_EVENT_DISPATCHER_WAKEUP,
                                           NULL, NULL);
    assert(initialized);
    app_dispatcher = &dispatcher;
    running_app = true;
    enum scrcpy_exit_code result = sc_app_run(&options, &dispatcher);
    running_app = false;
    bool fatal = app_case == APP_FATAL_DECODER || app_case == APP_FATAL_SERVER
        || app_case == APP_RECORDING_AFTER_TRANSIENT
        || app_case == APP_STARTUP_TIMEOUT
        || app_case == APP_RECORDING_LATE_FAILURE
        || app_case == APP_RECORDING_STOP_FAILURE;
    assert(result == (fatal ? SCRCPY_EXIT_FAILURE : SCRCPY_EXIT_SUCCESS));

    if (app_case == APP_INITIALIZATION_AFTER_RESOLVE_FAILURE) {
        assert(video_initializations == 1 && audio_initializations == 1
                && gamepad_initializations == 1);
        assert(servers == 2);
    }

    if (app_case == APP_SELECTED_NETWORK) {
        assert(!strcmp(first_server_serial, "127.0.0.1:43210"));
        assert(servers == 1 && atomic_load(&connected_queries) == 1);
    }

    if (app_case == APP_HEADLESS_REPLACEMENT) {
        assert(servers == 2 && resources[RESOURCE_VIDEO].started == 2);
        assert(!strcmp(first_server_serial, "fixture-usb"));
        assert(atomic_load(&connected_queries) == 1);
    }

    if (app_case == APP_FATAL_DECODER || app_case == APP_FATAL_SERVER
            || app_case == APP_DEADLINE_RETIRING || app_case == APP_STARTUP_TIMEOUT
            || app_case == APP_STARTUP_DEADLINE) {
        assert(servers == 1);
    }

    if (app_case == APP_STARTUP_TIMEOUT || app_case == APP_STARTUP_DEADLINE) {
        assert(!graph_start_attempts);
        assert(startup_waits >= (app_case == APP_STARTUP_TIMEOUT ? 2u : 1u));
    }

    if (app_case == APP_RECORDING_AFTER_TRANSIENT) {
        assert(servers == 2 && resources[RESOURCE_RECORDER].acquired == 1);
        assert(!strcmp(recording_filename,
                        "app-synthetic-never-opened.part01.mkv"));
    }

    if (app_case == APP_DEADLINE_RESOLVING || app_case == APP_STOP_RESOLVING
            || app_case == APP_STOP_WAITING) {
        assert(!servers && !dispatcher.last_generation);
    }

    if (app_case == APP_GENERATION_SOAK) {
        assert(servers == APP_SOAK_GENERATIONS && server_peak == 1);
        assert(dispatcher.last_generation == APP_SOAK_GENERATIONS);
        assert(resources[RESOURCE_VIDEO].started == APP_SOAK_GENERATIONS);
        printf("Application soak: %u real coordinator replacements, "
               "%u owners, server peak=%u; settled ledgers/dispatcher before "
               "every replacement (fake-clock stable health, no media backend)\n",
               APP_SOAK_REPLACEMENTS, APP_SOAK_GENERATIONS, server_peak);
    }

    if (real_recording_case()) {
        assert(servers == 1 && resources[RESOURCE_RECORDER].started == 1);
        assert(real_trailers == (app_case == APP_RECORDING_PRE_MEDIA_STOP ? 0 : 1));

        if (recording_probe) {
            assert(av_buffer_get_ref_count(recording_probe) == 1);
            av_buffer_unref(&recording_probe);
        }
        assert(!remove(recording_filename));
    }

    for (unsigned resource = 0; resource < RESOURCE_COUNT; ++resource) {
        assert(resources[resource].acquired == resources[resource].destroyed);
        assert(resources[resource].started == resources[resource].joined);
    }

    test_destroy_dispatcher(&dispatcher);
    app_dispatcher = NULL;
    fprintf(stderr, "App case=%u settled\n", selected_case);
}

int
main(int argument_count, char **arguments) {
    setvbuf(stderr, NULL, _IONBF, 0);
    bool initialized = SDL_Init(SDL_INIT_EVENTS);
    assert(initialized);

    if (argument_count == 2 && !strcmp(arguments[1], "--initialization")) {
        test_application_case(APP_INITIALIZATION_AFTER_RESOLVE_FAILURE);
    } else if (argument_count == 2 && !strcmp(arguments[1], "--late-recording")) {
        test_application_case(APP_RECORDING_LATE_FAILURE);
    } else if (argument_count == 2 && !strcmp(arguments[1], "--stop-recording-failure")) {
        test_application_case(APP_RECORDING_STOP_FAILURE);
    } else if (argument_count == 2 && !strcmp(arguments[1], "--soak")) {
        test_application_case(APP_GENERATION_SOAK);
    } else {
        for (enum app_case selected = APP_INITIALIZATION_AFTER_RESOLVE_FAILURE;
                selected <= APP_GENERATION_SOAK; ++selected) {
            test_application_case(selected);
        }
    }

    SDL_Quit();
    puts("Application connection: actual coordinator/owner policy cases passed");
    return 0;
}
