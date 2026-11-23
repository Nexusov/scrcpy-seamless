package com.genymobile.scrcpy.control;

import java.nio.ByteBuffer;
import java.nio.CharBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CharsetEncoder;
import java.nio.charset.CoderResult;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;

/** One strict scalar/NUL policy for textual wire fields, never binary HID data. */
final class DeviceProtocolText {
    private static final int VALIDATION_BUFFER_SIZE = 4096;

    private DeviceProtocolText() {
    }

    static String decode(byte[] bytes) throws ControlProtocolException {
        for (byte value : bytes) {

            if (value == 0) {
                throw new ControlProtocolException("NUL is not supported in device-protocol text");
            }
        }

        try {
            return StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT)
                    .onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString();
        } catch (CharacterCodingException invalid) {
            throw new ControlProtocolException("Malformed UTF-8 device-protocol text");
        }
    }

    private static void requireValid(CoderResult result) throws ControlProtocolException {

        if (result.isError()) {

            try {
                result.throwException();
            } catch (CharacterCodingException invalid) {
                throw new ControlProtocolException("Malformed Unicode device-protocol text");
            }
        }
    }

    /** Retain only the wire prefix while validating the complete caller-owned source. */
    static byte[] encode(String text, int maxBytes) throws ControlProtocolException {

        if (text == null || text.indexOf('\0') >= 0) {
            throw new ControlProtocolException("Absent or NUL-containing device-protocol text");
        }
        int capacity = (int) Math.min(maxBytes, (long) text.length() * 3);
        ByteBuffer prefix = ByteBuffer.allocate(capacity);
        CharBuffer input = CharBuffer.wrap(text);
        CharsetEncoder encoder = StandardCharsets.UTF_8.newEncoder().onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT);
        CoderResult result = encoder.encode(input, prefix, true);
        requireValid(result);
        byte[] retained = Arrays.copyOf(prefix.array(), prefix.position());
        ByteBuffer validation = result.isOverflow() ? ByteBuffer.allocate(VALIDATION_BUFFER_SIZE) : prefix;

        while (result.isOverflow()) {
            validation.clear();
            result = encoder.encode(input, validation, true);
            requireValid(result);
        }
        validation.clear();
        CoderResult flushed = encoder.flush(validation);
        requireValid(flushed);

        if (!flushed.isUnderflow()) {
            throw new ControlProtocolException("Unexpected UTF-8 encoder flush result");
        }
        return retained;
    }
}
