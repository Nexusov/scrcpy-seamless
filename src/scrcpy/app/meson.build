src = [
    'src/main.c',
    'src/adb/adb.c',
    'src/adb/adb_device.c',
    'src/adb/adb_parser.c',
    'src/adb/adb_tunnel.c',
    'src/audio_player.c',
    'src/audio_regulator.c',
    'src/cli.c',
    'src/clock.c',
    'src/compat.c',
    'src/control_msg.c',
    'src/controller.c',
    'src/dispatcher.c',
    'src/decoder.c',
    'src/delay_buffer.c',
    'src/demuxer.c',
    'src/device_msg.c',
    'src/disconnect.c',
    'src/events.c',
    'src/icon.c',
    'src/file_pusher.c',
    'src/fps_counter.c',
    'src/frame_buffer.c',
    'src/video_ingress.c',
    'src/input_manager.c',
    'src/keyboard_sdk.c',
    'src/mouse_capture.c',
    'src/mouse_sdk.c',
    'src/opengl.c',
    'src/options.c',
    'src/packet_merger.c',
    'src/receiver.c',
    'src/recorder.c',
    'src/scrcpy.c',
    'src/session.c',
    'src/session_notifications.c',
    'src/screen.c',
    'src/sdl_hints.c',
    'src/server.c',
    'src/texture.c',
    'src/version.c',
    'src/hid/hid_gamepad.c',
    'src/hid/hid_keyboard.c',
    'src/hid/hid_mouse.c',
    'src/trait/frame_source.c',
    'src/trait/packet_source.c',
    'src/uhid/gamepad_uhid.c',
    'src/uhid/keyboard_uhid.c',
    'src/uhid/mouse_uhid.c',
    'src/uhid/uhid_output.c',
    'src/util/acksync.c',
    'src/util/audiobuf.c',
    'src/util/average.c',
    'src/util/env.c',
    'src/util/file.c',
    'src/util/intmap.c',
    'src/util/intr.c',
    'src/util/launcher_stop.c',
    'src/util/log.c',
    'src/util/memory.c',
    'src/util/net.c',
    'src/util/net_intr.c',
    'src/util/process.c',
    'src/util/process_intr.c',
    'src/util/rand.c',
    'src/util/sdl.c',
    'src/util/strbuf.c',
    'src/util/str.c',
    'src/util/term.c',
    'src/util/thread.c',
    'src/util/tick.c',
    'src/util/timeout.c',
]

feature_test_macros = [
    '-D_GNU_SOURCE',
    '-D_POSIX_C_SOURCE=200809L',
    '-D_XOPEN_SOURCE=700',
]

conf = configuration_data()

if host_machine.system() == 'windows'
    windows = import('windows')
    src += [
        'src/util/command.c',
        'src/sys/win/file.c',
        'src/sys/win/process.c',
        'src/ipc/ipc_frame.c',
        'src/ipc/ipc_json.c',
        'src/ipc/machine_exit.c',
        'src/ipc/machine.c',
        'vendor/yyjson/yyjson.c',
        windows.compile_resources('scrcpy-windows.rc'),
    ]
    feature_test_macros += [
        '-D_WIN32_WINNT=0x0600',
        '-DWINVER=0x0600',
    ]
else
    src += [
        'src/sys/unix/file.c',
        'src/sys/unix/process.c',
    ]
    if host_machine.system() == 'darwin'
        feature_test_macros += '-D_DARWIN_C_SOURCE'
    endif
endif


add_project_arguments(feature_test_macros, language: 'c')

v4l2_support = get_option('v4l2') and host_machine.system() == 'linux'
if v4l2_support
    src += [ 'src/v4l2_sink.c' ]
endif

usb_support = get_option('usb')
if usb_support
    src += [
        'src/usb/aoa_hid.c',
        'src/usb/gamepad_aoa.c',
        'src/usb/keyboard_aoa.c',
        'src/usb/mouse_aoa.c',
        'src/usb/scrcpy_otg.c',
        'src/usb/usb.c',
    ]
endif

cc = meson.get_compiler('c')

static = get_option('static')

dependencies = [
    dependency('libavformat', version: '>= 57.33', static: static),
    dependency('libavcodec', version: '>= 57.37', static: static),
    dependency('libavutil', static: static),
    dependency('libswresample', static: static),
    dependency('sdl3', version: '>= 3.2.0', static: static),
]

if v4l2_support
    dependencies += dependency('libavdevice', static: static)
endif

if usb_support
    dependencies += dependency('libusb-1.0', static: static)
endif

if host_machine.system() == 'windows'
    dependencies += cc.find_library('mingw32')
    dependencies += cc.find_library('ws2_32')
    dependencies += cc.find_library('rpcrt4')
endif

check_functions = [
    'strdup',
    'asprintf',
    'vasprintf',
    'nrand48',
    'jrand48',
    'reallocarray',
]

foreach f : check_functions
  if cc.has_function(f)
    define = 'HAVE_' + f.underscorify().to_upper()
    conf.set(define, true)
  endif
endforeach

conf.set('HAVE_SOCK_CLOEXEC', host_machine.system() != 'windows' and
                              cc.has_header_symbol('sys/socket.h', 'SOCK_CLOEXEC'))

# the version, updated on release
conf.set_quoted('SCRCPY_VERSION', meson.project_version())

# the prefix used during configuration (meson --prefix=PREFIX)
conf.set_quoted('PREFIX', get_option('prefix'))

# build a "portable" version (with scrcpy-server accessible from the same
# directory as the executable)
conf.set('PORTABLE', get_option('portable'))

# the default client TCP port range for the "adb reverse" tunnel
# overridden by option --port
conf.set('DEFAULT_LOCAL_PORT_RANGE_FIRST', '27183')
conf.set('DEFAULT_LOCAL_PORT_RANGE_LAST', '27199')

# run a server debugger and wait for a client to be attached
conf.set('SERVER_DEBUGGER', get_option('server_debugger'))

# enable V4L2 support (linux only)
conf.set('HAVE_V4L2', v4l2_support)

# enable HID over AOA support (linux only)
conf.set('HAVE_USB', usb_support)

configure_file(configuration: conf, output: 'config.h')

src_dir = include_directories('src')

executable('scrcpy', src,
           dependencies: dependencies,
           include_directories: src_dir,
           install: true,
           c_args: [])

# <https://mesonbuild.com/Builtin-options.html#directories>
datadir = get_option('datadir') # by default 'share'

install_man('scrcpy.1')
install_data('data/scrcpy.png',
             install_dir: datadir / 'icons/hicolor/256x256/apps')
install_data('data/disconnected.png',
             install_dir: datadir / 'icons/hicolor/256x256/apps')
install_data('data/zsh-completion/_scrcpy',
             install_dir: datadir / 'zsh/site-functions')
install_data('data/bash-completion/scrcpy',
             install_dir: datadir / 'bash-completion/completions')

# Desktop entry file for application launchers
if host_machine.system() == 'linux'
    # Install a launcher (ex: /usr/local/share/applications/scrcpy.desktop)
    install_data('data/scrcpy.desktop',
                 install_dir: datadir / 'applications')
    install_data('data/scrcpy-console.desktop',
                 install_dir: datadir / 'applications')
endif


### TESTS

# do not build tests in release (assertions would not be executed at all)
if get_option('buildtype') == 'debug'
    # Link shipping ownership and orchestration; only external effects are wrapped.
    # These Windows cases also cover the existing machine Stop intent boundary.
    if host_machine.system() == 'windows'
        session_sources = []
        foreach index : range(1, src.length())
            session_sources += src[index]
        endforeach
        session_wraps = [
            'sc_server_init', 'sc_server_start', 'sc_server_stop',
            'sc_server_join', 'sc_server_destroy', 'sc_machine_is_active',
            'sc_machine_stop_requested', 'sc_machine_emit_lifecycle',
            'sc_controller_init', 'sc_controller_start', 'sc_controller_join',
            'sc_controller_destroy', 'sc_file_pusher_init',
            'sc_file_pusher_destroy', 'sc_screen_init', 'sc_recorder_init',
            'sc_recorder_start', 'sc_recorder_destroy', 'sc_demuxer_start',
            'sc_demuxer_join', 'sc_timeout_init', 'sc_timeout_start',
            'sc_timeout_destroy', 'sc_acksync_init', 'sc_acksync_destroy',
            'sc_usb_init', 'sc_usb_select_device', 'sc_usb_connect',
            'sc_usb_device_destroy', 'sc_usb_stop', 'sc_usb_join',
            'sc_usb_disconnect', 'sc_usb_destroy', 'sc_aoa_init',
            'sc_aoa_start', 'sc_aoa_join', 'sc_aoa_destroy',
            'sc_keyboard_aoa_init', 'sc_keyboard_aoa_destroy',
            'sc_mouse_aoa_init', 'sc_mouse_aoa_destroy',
            'SDL_GetJoysticks', 'SDL_IsGamepad', 'SDL_free',
            'calloc', 'free', 'sc_mutex_init',
        ]
        session_link_args = []
        foreach symbol : session_wraps
            session_link_args += '-Wl,--wrap=' + symbol
        endforeach
        session_test = executable('test_session',
            ['tests/test_session.c'] + session_sources,
            include_directories: src_dir, dependencies: dependencies,
            link_args: session_link_args)
        test('test_session', session_test)

        if usb_support
            ack_wraps = [
                'sc_server_init', 'sc_server_start', 'sc_server_stop',
                'sc_server_join', 'sc_server_destroy', 'sc_usb_init',
                'sc_usb_select_device', 'sc_usb_connect', 'sc_usb_stop',
                'sc_usb_join', 'sc_usb_disconnect', 'sc_usb_destroy',
                'libusb_control_transfer', 'sc_aoa_start', 'sc_aoa_join',
                'sc_acksync_wait', 'net_recv', 'net_send_all',
                'sc_controller_join', 'sc_acksync_destroy',
            ]
            ack_link_args = []
            foreach symbol : ack_wraps
                ack_link_args += '-Wl,--wrap=' + symbol
            endforeach
            ack_test = executable('test_session_ack',
                ['tests/test_session_ack.c'] + session_sources,
                include_directories: src_dir, dependencies: dependencies,
                link_args: ack_link_args)
            test('test_session_ack', ack_test)
            otg_test = executable('test_otg_lifetime', [
                'tests/test_otg_lifetime.c', 'src/usb/scrcpy_otg.c',
                'src/session_notifications.c', 'src/dispatcher.c', 'src/events.c',
                'src/util/thread.c', 'src/util/tick.c', 'src/util/log.c',
                'src/compat.c',
            ], include_directories: src_dir, dependencies: dependencies)
            test('test_otg_lifetime', otg_test)
        endif
    endif

    tests = [
        ['test_session_sink_graph', [
            'tests/test_session_sink_graph.c', 'src/decoder.c',
            'src/trait/packet_source.c', 'src/trait/frame_source.c',
            'src/util/log.c',
        ]],
        ['test_file_pusher_lifetime', [
            'tests/test_file_pusher_lifetime.c', 'src/util/intr.c',
            'src/util/memory.c', 'src/util/thread.c', 'src/util/tick.c',
            'src/util/log.c',
        ]],
        ['test_session_notifications', [
            'tests/test_session_notifications.c',
            'src/session_notifications.c', 'src/dispatcher.c', 'src/events.c',
            'src/util/thread.c', 'src/util/tick.c', 'src/util/log.c',
        ]],
        ['test_input_binding', [
            'tests/test_input_binding.c',
            'src/input_manager.c',
            'src/options.c',
        ]],
        ['test_dispatcher', [
            'tests/test_dispatcher.c',
            'src/dispatcher.c',
            'src/events.c',
            'src/util/thread.c',
            'src/util/tick.c',
            'src/util/log.c',
        ]],
        ['test_thread_timedwait', [
            'tests/test_thread_timedwait.c',
        ]],
        ['test_adb_parser', [
            'tests/test_adb_parser.c',
            'src/adb/adb_device.c',
            'src/adb/adb_parser.c',
            'src/util/str.c',
            'src/util/strbuf.c',
        ]],
        ['test_launcher_stop', [
            'tests/test_launcher_stop.c',
            'src/util/launcher_stop.c',
            'src/events.c',
            'src/util/thread.c',
            'src/util/tick.c',
            'src/util/log.c',
        ]],
        ['test_event_payload', [
            'tests/test_event_payload.c',
            'src/events.c',
            'src/util/thread.c',
            'src/util/tick.c',
            'src/util/log.c',
        ]],
        ['test_memory', [
            'tests/test_memory.c',
            'src/util/memory.c',
        ]],
        ['test_binary', [
            'tests/test_binary.c',
        ]],
        ['test_audiobuf', [
            'tests/test_audiobuf.c',
            'src/util/audiobuf.c',
            'src/util/memory.c',
        ]],
        ['test_cli', [
            'tests/test_cli.c',
            'src/cli.c',
            'src/options.c',
            'src/util/log.c',
            'src/util/net.c',
            'src/util/str.c',
            'src/util/strbuf.c',
            'src/util/term.c',
        ]],
        ['test_command_windows', [
          'tests/test_command_windows.c',
          'src/util/command.c',
          'src/util/str.c',
          'src/util/strbuf.c',
        ]],
        ['test_control_msg_serialize', [
            'tests/test_control_msg_serialize.c',
            'src/control_msg.c',
            'src/util/str.c',
            'src/util/strbuf.c',
        ]],
        ['test_device_msg_deserialize', [
            'tests/test_device_msg_deserialize.c',
            'src/device_msg.c',
        ]],
        ['test_orientation', [
            'tests/test_orientation.c',
            'src/options.c',
        ]],
        ['test_strbuf', [
            'tests/test_strbuf.c',
            'src/util/strbuf.c',
        ]],
        ['test_str', [
            'tests/test_str.c',
            'src/util/str.c',
            'src/util/strbuf.c',
        ]],
        ['test_vecdeque', [
            'tests/test_vecdeque.c',
            'src/util/memory.c',
        ]],
        ['test_vector', [
            'tests/test_vector.c',
        ]],
    ]

    foreach t : tests
        sources = t[1] + ['src/compat.c']
        exe = executable(t[0], sources,
                         include_directories: src_dir,
                         dependencies: dependencies,
                         c_args: ['-DSC_TEST'])
        test(t[0], exe)
    endforeach

    # Exercise actual SDL dummy callback exclusion and production open unwind.
    timeout_lifetime_test = executable('test_timeout_lifetime', [
        'tests/test_timeout_lifetime.c', 'src/util/thread.c',
        'src/util/tick.c', 'src/util/log.c', 'src/compat.c',
    ], include_directories: src_dir, dependencies: dependencies,
       c_args: ['-DSC_TEST'])
    test('test_timeout_lifetime', timeout_lifetime_test)
    test('test_timeout_partial_init', timeout_lifetime_test, args: ['--init-failure'])
    test('test_timeout_stopped_context', timeout_lifetime_test, args: ['--already-stopped'])

    # Exercise actual SDL dummy callback exclusion and production open unwind.
    audio_lifetime_test = executable('test_audio_lifetime', [
        'tests/test_audio_lifetime.c', 'src/audio_player.c',
        'src/audio_regulator.c', 'src/util/audiobuf.c', 'src/util/average.c',
        'src/util/memory.c', 'src/util/thread.c', 'src/util/tick.c',
        'src/util/log.c', 'src/compat.c',
    ], include_directories: src_dir, dependencies: dependencies,
       c_args: ['-DSC_TEST'],
       link_args: ['-Wl,--wrap=swr_alloc', '-Wl,--wrap=swr_init',
                   '-Wl,--wrap=swr_free', '-Wl,--wrap=sc_mutex_init',
                   '-Wl,--wrap=sc_mutex_destroy', '-Wl,--wrap=sc_allocarray',
                   '-Wl,--wrap=malloc', '-Wl,--wrap=free',
                   '-Wl,--wrap=sc_audiobuf_destroy',
                   '-Wl,--wrap=sc_audio_regulator_destroy',
                   '-Wl,--wrap=sc_audio_regulator_pull',
                   '-Wl,--wrap=SDL_SetHint', '-Wl,--wrap=SDL_OpenAudioDeviceStream',
                   '-Wl,--wrap=SDL_ResumeAudioDevice',
                   '-Wl,--wrap=SDL_PauseAudioDevice',
                   '-Wl,--wrap=SDL_DestroyAudioStream'])
    test('test_audio_lifetime', audio_lifetime_test)

    # Exercise real FFmpeg reference ownership and allocation failures.
    framebuffer_test = executable('test_frame_buffer', [
        'tests/test_frame_buffer.c', 'src/frame_buffer.c', 'src/compat.c',
    ], include_directories: src_dir, dependencies: dependencies,
       c_args: ['-DSC_TEST'],
       link_args: ['-Wl,--wrap=av_frame_alloc', '-Wl,--wrap=av_frame_free'])
    test('test_frame_buffer', framebuffer_test)

    # Exercise production ingress and bounded notification/frame ownership.
    ingress_test = executable('test_video_ingress', [
        'tests/test_video_ingress.c', 'src/video_ingress.c',
        'src/frame_buffer.c', 'src/dispatcher.c', 'src/events.c',
        'src/util/thread.c', 'src/util/tick.c', 'src/util/log.c', 'src/compat.c',
    ], include_directories: src_dir, dependencies: dependencies,
       c_args: ['-DSC_TEST'],
       link_args: ['-Wl,--wrap=av_frame_alloc', '-Wl,--wrap=av_frame_free',
                   '-Wl,--wrap=malloc', '-Wl,--wrap=free'])
    test('test_video_ingress', ingress_test)

    # Link the real delayed producer path and gate scheduling at external waits.
    delay_ingress_test = executable('test_delay_buffer_ingress', [
        'tests/test_delay_buffer_ingress.c', 'src/delay_buffer.c',
        'src/clock.c', 'src/trait/frame_source.c', 'src/video_ingress.c',
        'src/frame_buffer.c', 'src/dispatcher.c', 'src/events.c',
        'src/util/thread.c', 'src/util/tick.c', 'src/util/log.c',
        'src/util/memory.c', 'src/compat.c',
    ], include_directories: src_dir, dependencies: dependencies,
       c_args: ['-DSC_TEST'],
       link_args: ['-Wl,--wrap=av_frame_alloc', '-Wl,--wrap=av_frame_free',
                   '-Wl,--wrap=sc_tick_now', '-Wl,--wrap=sc_cond_timedwait',
                   '-Wl,--wrap=sc_thread_join', '-Wl,--wrap=free',
                   '-Wl,--wrap=av_frame_ref', '-Wl,--wrap=reallocarray',
                   '-Wl,--wrap=sc_thread_create'])
    test('test_delay_buffer_ingress', delay_ingress_test)

    # Exercise production presentation with deterministic external-effect doubles.
    presentation_test = executable('test_screen_presentation', [
        'tests/test_screen_presentation.c', 'src/screen.c',
        'src/input_manager.c', 'src/video_ingress.c', 'src/frame_buffer.c',
        'src/dispatcher.c', 'src/events.c', 'src/options.c', 'src/control_msg.c',
        'src/fps_counter.c', 'src/disconnect.c', 'src/util/thread.c',
        'src/util/tick.c', 'src/util/log.c', 'src/util/str.c',
        'src/util/strbuf.c', 'src/compat.c',
    ], include_directories: src_dir, dependencies: dependencies,
       c_args: ['-DSC_TEST', '-DSC_SCREEN_TEST'],
       link_args: ['-Wl,--wrap=av_frame_alloc', '-Wl,--wrap=av_frame_free',
                   '-Wl,--wrap=sc_disconnect_start',
                   '-Wl,--wrap=sc_disconnect_interrupt',
                   '-Wl,--wrap=sc_disconnect_join',
                   '-Wl,--wrap=sc_disconnect_destroy',
                   '-Wl,--wrap=SDL_WaitEventTimeout'])
    test('test_screen_presentation', presentation_test)

    # Link actual receiver/deserializer ownership with external-effect doubles.
    receiver_test = executable('test_receiver_dispatch', [
        'tests/test_receiver_dispatch.c',
        'src/receiver.c',
        'src/dispatcher.c',
        'src/device_msg.c',
        'src/util/acksync.c',
        'src/util/thread.c',
        'src/util/tick.c',
        'src/util/log.c',
        'src/util/str.c',
        'src/util/strbuf.c',
        'src/compat.c',
    ], include_directories: src_dir,
       dependencies: dependencies,
       c_args: ['-DSC_TEST'],
       link_args: ['-Wl,--wrap=malloc', '-Wl,--wrap=free'])
    test('test_receiver_dispatch', receiver_test)

    ipc_test = executable('test_ipc_contract', [
        'tests/test_ipc_contract.c',
        'src/ipc/ipc_frame.c',
        'src/ipc/ipc_json.c',
        'vendor/yyjson/yyjson.c',
    ], include_directories: src_dir,
       c_args: ['-DSC_TEST'])
    test('test_ipc_contract', ipc_test,
         args: ['--golden', meson.project_source_root() / '../../spec/desktop-native/golden'])

    if host_machine.system() == 'windows'
        machine_exit_test = executable('test_machine_exit', [
            'tests/test_machine_exit.c',
            'src/ipc/machine_exit.c',
        ], include_directories: src_dir,
           dependencies: [dependency('sdl3')],
           c_args: ['-DSC_TEST'])
        test('test_machine_exit', machine_exit_test)

        executable('test_machine_child', [
            'tests/test_machine_child.c',
            'src/ipc/machine.c',
            'src/ipc/ipc_frame.c',
            'src/ipc/ipc_json.c',
            'vendor/yyjson/yyjson.c',
        ], include_directories: src_dir,
           dependencies: [dependency('sdl3'), cc.find_library('rpcrt4')],
           c_args: ['-DSC_TEST'])
    endif
endif

if meson.version().version_compare('>= 0.58.0')
       devenv = environment()
       devenv.set('SCRCPY_ICON_DIR', meson.current_source_dir() / 'data')
       meson.add_devenv(devenv)
endif
