#include "transport_resolver.h"

#include <assert.h>
#include <stdlib.h>
#include <string.h>
#include <SDL3/SDL_thread.h>
#include "adb/adb.h"

static bool
sc_transport_copy_token(char *target, const char *source, size_t length) {

    if (!length || length > SC_TRANSPORT_IDENTITY_MAX) {
        return false;
    }

    for (size_t index = 0; index < length; ++index) {

        if ((unsigned char) source[index] <= ' ') {
            return false;
        }
    }
    memcpy(target, source, length);
    target[length] = '\0';
    return true;
}

bool
sc_transport_parse_mdns(const char *output, const char *service,
                         char endpoint[SC_TRANSPORT_IDENTITY_MAX + 1]) {
    static const char type[] = "_adb-tls-connect._tcp";
    unsigned matches = 0;
    char selected[SC_TRANSPORT_IDENTITY_MAX + 1];
    size_t service_length = strlen(service);

    while (service_length && service[service_length - 1] == '.') {
        --service_length;
    }

    /* ADB reports instance, service type and endpoint as three tokens. */
    for (const char *line = output; *line;) {
        const char *end = line + strcspn(line, "\r\n");
        const char *tokens[3];
        size_t lengths[3];
        const char *cursor = line;
        unsigned count = 0;

        while (cursor < end && count < 3) {
            cursor += strspn(cursor, " \t");

            if (cursor >= end) {
                break;
            }
            tokens[count] = cursor;

            while (cursor < end && *cursor != ' ' && *cursor != '\t') {
                ++cursor;
            }
            lengths[count] = (size_t) (cursor - tokens[count]);
            ++count;
        }

        if (count == 3) {

            while (lengths[0] && tokens[0][lengths[0] - 1] == '.') {
                --lengths[0];
            }

            while (lengths[1] && tokens[1][lengths[1] - 1] == '.') {
                --lengths[1];
            }
            bool type_matches = lengths[1] == sizeof(type) - 1
                             && !memcmp(tokens[1], type, sizeof(type) - 1);
            bool instance_matches = lengths[0] == service_length
                                  && !memcmp(tokens[0], service, service_length);
            /* Persisted Core identity may include the type suffix. */
            bool full_matches = service_length == lengths[0] + 1 + lengths[1]
                             && !memcmp(service, tokens[0], lengths[0])
                             && service[lengths[0]] == '.'
                             && !memcmp(service + lengths[0] + 1, tokens[1],
                                        lengths[1]);

            if (type_matches && (instance_matches || full_matches)) {
                cursor += strspn(cursor, " \t");

                if (cursor != end || ++matches > 1
                        || !sc_transport_copy_token(selected, tokens[2],
                                                     lengths[2])
                        || !sc_connection_endpoint_valid(selected)) {
                    return false;
                }
            }
        }
        line = end + strspn(end, "\r\n");
    }

    if (matches != 1) {
        return false;
    }
    strcpy(endpoint, selected);
    return true;
}

static int
sc_transport_resolve(void *userdata) {
    struct sc_transport_resolver *resolver = userdata;
    const struct sc_connection_target *candidate = &resolver->candidate;
    struct sc_transport_result *result = &resolver->result;
    result->failure = SC_CONNECTION_FAILURE_TRANSIENT_CONNECT;
    const char *adb = sc_adb_get_executable();
    char *output = malloc(SC_TRANSPORT_QUERY_BYTES);

    if (!output) {
        result->failure = SC_CONNECTION_FAILURE_SERVER_START;
        goto complete;
    }
    char target[SC_TRANSPORT_IDENTITY_MAX + 1];

    if (candidate->transport == SC_CONNECTION_USB) {
        const char *const path_argv[] = {adb, "-s", candidate->serial,
                                         "get-devpath", NULL};
        bool positive = sc_adb_query(&resolver->intr, path_argv, output,
                                      SC_TRANSPORT_QUERY_BYTES);
        size_t length = positive ? strcspn(output, "\r\n") : 0;
        positive = positive && !output[length + strspn(output + length, "\r\n")]
                && sc_transport_copy_token(target, output, length)
                && strcmp(target, "unknown");

        if (sc_intr_is_interrupted(&resolver->intr)) {
            goto free_output;
        }

        if (!positive) {
            /* Legacy Windows backends may omit devpath; the USB-scoped single
             * route query supplies evidence without guessing serial shape. */
            const char *const argv[] = {adb, "-d", "get-serialno", NULL};

            if (!sc_adb_query(&resolver->intr, argv, output,
                               SC_TRANSPORT_QUERY_BYTES)) {
                goto free_output;
            }
            length = strcspn(output, "\r\n");

            if (output[length + strspn(output + length, "\r\n")]
                    || strlen(candidate->serial) != length
                    || memcmp(output, candidate->serial, length)) {
                goto free_output;
            }
        }
        const char *const state_argv[] = {adb, "-s", candidate->serial,
                                          "get-state", NULL};

        if (!sc_adb_query(&resolver->intr, state_argv, output,
                           SC_TRANSPORT_QUERY_BYTES)
                || strncmp(output, "device", 6)
                || output[6 + strspn(output + 6, "\r\n")]) {
            goto free_output;
        }
        strcpy(target, candidate->serial);
    } else if (result->attempt == 1 && candidate->serial[0]) {
        const char *const argv[] = {adb, "-s", candidate->serial,
                                    "get-state", NULL};

        if (!sc_adb_query(&resolver->intr, argv, output, SC_TRANSPORT_QUERY_BYTES)
                || strncmp(output, "device", 6)
                || output[6 + strspn(output + 6, "\r\n")]) {
            goto free_output;
        }
        strcpy(target, candidate->serial);
    } else {
        bool found = false;

        if (candidate->service[0]) {
            const char *const argv[] = {adb, "mdns", "services", NULL};
            found = sc_adb_query(&resolver->intr, argv, output,
                                  SC_TRANSPORT_QUERY_BYTES)
                 && sc_transport_parse_mdns(output, candidate->service, target);
        }

        if (sc_intr_is_interrupted(&resolver->intr)) {
            goto free_output;
        }

        if (!found) {

            if (!candidate->endpoint[0]) {
                goto free_output;
            }
            strcpy(target, candidate->endpoint);
        }

        const char *const argv[] = {adb, "connect", target, NULL};

        if (!sc_adb_query(&resolver->intr, argv, output,
                           SC_TRANSPORT_QUERY_BYTES)
                || (strncmp(output, "connected to ", 13)
                    && strncmp(output, "already connected to ", 21))) {
            goto free_output;
        }
    }
    strcpy(result->serial, target);
    result->failure = SC_CONNECTION_FAILURE_NONE;

free_output:
    free(output);
complete:
    atomic_store_explicit(&resolver->completed, true, memory_order_release);
    return 0;
}

bool
sc_transport_resolver_start(struct sc_transport_resolver *resolver,
                             sc_connection_attempt attempt,
                             const struct sc_connection_target *candidate) {
    memset(resolver, 0, sizeof(*resolver));

    if (!sc_intr_init(&resolver->intr)) {
        return false;
    }
    resolver->candidate = *candidate;
    resolver->result.attempt = attempt;
    atomic_init(&resolver->completed, false);

    if (!sc_thread_create(&resolver->thread, sc_transport_resolve,
                           "transport", resolver)) {
        sc_intr_destroy(&resolver->intr);
        return false;
    }
    resolver->started = true;
    return true;
}

void
sc_transport_resolver_cancel(struct sc_transport_resolver *resolver) {
    assert(resolver->started);
    sc_intr_interrupt(&resolver->intr);
}

bool
sc_transport_resolver_take(struct sc_transport_resolver *resolver,
                            struct sc_transport_result *result) {
    assert(resolver->started);

    if (!atomic_load_explicit(&resolver->completed, memory_order_acquire)
            || SDL_GetThreadState(resolver->thread.thread)
                != SDL_THREAD_COMPLETE) {
        return false;
    }
    sc_thread_join(&resolver->thread, NULL);
    sc_intr_destroy(&resolver->intr);
    *result = resolver->result;
    resolver->started = false;
    return true;
}
