package com.genymobile.scrcpy.control;

import com.genymobile.scrcpy.model.Position;

import org.junit.Assert;
import org.junit.Test;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.DataOutputStream;
import java.io.EOFException;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.nio.file.StandardOpenOption;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** The expected bytes and decoded fields are authored independently of both codecs. */
public class DeviceProtocolContractTest {
    private static final int WIRE_MAX = 262144;
    private static final int CASE_LIMIT = 64;
    private static final int CONTROL_CASES = 30;
    private static final int DEVICE_CASES = 10;
    private static final long SEQUENCE_BITS = 0xfedcba9876543210L;

    private static final class Frame {
        private final String id;
        private final byte[] bytes;
        private final int prefixLength;

        Frame(String id, byte[] bytes, int prefixLength) {
            this.id = id;
            this.bytes = bytes;
            this.prefixLength = prefixLength;
        }
    }

    private static Path specification() {
        String explicit = System.getenv("SC_DEVICE_PROTOCOL_SPEC");

        if (explicit != null) {
            return Paths.get(explicit);
        }
        Path root = Paths.get(System.getProperty("user.dir")).toAbsolutePath();

        for (int depth = 0; root != null && depth < 8; ++depth, root = root.getParent()) {
            Path candidate = root.resolve("spec/device-protocol/golden");

            if (Files.isDirectory(candidate)) {
                return candidate;
            }
        }
        throw new AssertionError("Device-protocol golden directory was not found");
    }

    private static byte[] hex(String text) {
        Assert.assertEquals(0, text.length() % 2);
        Assert.assertTrue(text.length() <= WIRE_MAX * 2);
        byte[] result = new byte[text.length() / 2];

        for (int index = 0; index < result.length; ++index) {
            int high = Character.digit(text.charAt(index * 2), 16);
            int low = Character.digit(text.charAt(index * 2 + 1), 16);
            Assert.assertTrue(high >= 0 && low >= 0);
            result[index] = (byte) ((high << 4) | low);
        }
        return result;
    }

    private static String hex(byte[] bytes) {
        StringBuilder output = new StringBuilder(bytes.length * 2);

        for (byte value : bytes) {
            output.append(Character.forDigit((value >>> 4) & 15, 16));
            output.append(Character.forDigit(value & 15, 16));
        }
        return output.toString();
    }

    private static List<String[]> rows(Path path) throws IOException {
        Assert.assertTrue(Files.size(path) <= (long) CASE_LIMIT * (WIRE_MAX * 2 + 128));
        List<String[]> result = new ArrayList<>();

        for (String line : Files.readAllLines(path, StandardCharsets.UTF_8)) {

            if (!line.isEmpty() && !line.startsWith("#")) {
                result.add(line.split("\t", -1));
                Assert.assertTrue(result.size() <= CASE_LIMIT);
            }
        }
        return result;
    }

    private static Map<String, Frame> goldens(String direction) throws IOException {
        Path directory = specification();
        Map<String, Frame> result = new LinkedHashMap<>();

        for (String[] row : rows(directory.resolve(direction + ".tsv"))) {
            Assert.assertEquals(2, row.length);
            byte[] bytes = hex(row[1]);
            Assert.assertNull(result.put(row[0], new Frame(row[0], bytes, bytes.length)));
        }

        for (String[] row : rows(directory.resolve("boundary.tsv"))) {
            Assert.assertEquals(5, row.length);

            if (row[0].equals(direction)) {
                byte[] prefix = hex(row[2]);
                byte[] repeated = hex(row[3]);
                int count = Integer.parseInt(row[4]);
                Assert.assertEquals(1, repeated.length);
                Assert.assertTrue(count >= 0 && count <= WIRE_MAX - prefix.length);
                byte[] bytes = Arrays.copyOf(prefix, prefix.length + count);
                Arrays.fill(bytes, prefix.length, bytes.length, repeated[0]);
                Assert.assertNull(result.put(row[1], new Frame(row[1], bytes, prefix.length)));
            }
        }
        return result;
    }

    private static void assertPosition(Position position, int x, int y, int width, int height) {
        Assert.assertEquals(x, position.getPoint().getX());
        Assert.assertEquals(y, position.getPoint().getY());
        Assert.assertEquals(width, position.getScreenSize().getWidth());
        Assert.assertEquals(height, position.getScreenSize().getHeight());
    }

    private static void assertRepeated(String text, int size) {
        Assert.assertEquals(size, text.length());

        for (int index = 0; index < size; ++index) {
            Assert.assertEquals('x', text.charAt(index));
        }
    }

    private static void assertControl(String id, ControlMessage message) {
        switch (id) {
            case "keycode":
                Assert.assertEquals(0, message.getType());
                Assert.assertEquals(1, message.getAction());
                Assert.assertEquals(42, message.getKeycode());
                Assert.assertEquals(0x01020304, message.getRepeat());
                Assert.assertEquals(0x11223344, message.getMetaState());
                return;
            case "text":
            case "text-empty":
            case "text-max":
            case "text-utf8-cut":
                Assert.assertEquals(1, message.getType());

                if (id.equals("text")) {
                    Assert.assertEquals("A\u20ac", message.getText());
                } else if (id.equals("text-empty")) {
                    Assert.assertEquals("", message.getText());
                } else {
                    assertRepeated(message.getText(), id.equals("text-max") ? 300 : 299);
                }
                return;
            case "touch":
            case "touch-pressure-one":
                Assert.assertEquals(2, message.getType());
                Assert.assertEquals(2, message.getAction());
                Assert.assertEquals(SEQUENCE_BITS, message.getPointerId());
                assertPosition(message.getPosition(), -123, 456, 1080, 1920);
                Assert.assertEquals(id.equals("touch") ? 0.5f : 1f, message.getPressure(), 0);
                Assert.assertEquals(1, message.getActionButton());
                Assert.assertEquals(5, message.getButtons());
                return;
            case "scroll":
                Assert.assertEquals(3, message.getType());
                assertPosition(message.getPosition(), 42, 99, 800, 600);
                Assert.assertEquals(-16f, message.getHScroll(), 0);
                Assert.assertEquals(8f, message.getVScroll(), 0);
                Assert.assertEquals(1, message.getButtons());
                return;
            case "back":
                Assert.assertEquals(4, message.getType());
                Assert.assertEquals(0, message.getAction());
                return;
            case "get-clipboard":
                Assert.assertEquals(8, message.getType());
                Assert.assertEquals(2, message.getCopyKey());
                return;
            case "set-clipboard":
            case "set-clipboard-empty":
            case "set-clipboard-max":
            case "set-clipboard-utf8-cut":
                Assert.assertEquals(9, message.getType());
                boolean empty = id.equals("set-clipboard-empty");
                Assert.assertEquals(empty ? 0 : SEQUENCE_BITS, message.getSequence());
                Assert.assertEquals(!empty, message.getPaste());

                if (id.equals("set-clipboard")) {
                    Assert.assertEquals("A\u20ac", message.getText());
                } else if (empty) {
                    Assert.assertEquals("", message.getText());
                } else {
                    assertRepeated(message.getText(), id.equals("set-clipboard-max") ? 262130 : 262129);
                }
                return;
            case "display-power":
            case "display-power-off":
                Assert.assertEquals(10, message.getType());
                Assert.assertEquals(id.equals("display-power"), message.getOn());
                return;
            case "uhid-create":
                Assert.assertEquals(12, message.getType());
                Assert.assertEquals(0x1234, message.getId());
                Assert.assertEquals(0x0abc, message.getVendorId());
                Assert.assertEquals(0x00ff, message.getProductId());
                Assert.assertEquals("U", message.getText());
                Assert.assertArrayEquals(new byte[] {5, 1, 9}, message.getData());
                return;
            case "uhid-input":
                Assert.assertEquals(13, message.getType());
                Assert.assertEquals(0x1234, message.getId());
                Assert.assertArrayEquals(new byte[] {0, -1, -128}, message.getData());
                return;
            case "uhid-destroy":
                Assert.assertEquals(14, message.getType());
                Assert.assertEquals(0x1234, message.getId());
                return;
            case "start-app":
                Assert.assertEquals(16, message.getType());
                Assert.assertEquals("org.example.demo", message.getText());
                return;
            case "camera-torch":
                Assert.assertEquals(18, message.getType());
                Assert.assertTrue(message.getOn());
                return;
            case "resize-display":
                Assert.assertEquals(21, message.getType());
                Assert.assertEquals(2560, message.getWidth());
                Assert.assertEquals(1440, message.getHeight());
                return;
            default:
                String[] emptyNames = {"expand-notifications", "expand-settings", "collapse-panels", "rotate",
                    "keyboard-settings", "reset-video", "camera-zoom-in", "camera-zoom-out"};
                int[] emptyTypes = {5, 6, 7, 11, 15, 17, 19, 20};

                for (int index = 0; index < emptyNames.length; ++index) {

                    if (id.equals(emptyNames[index])) {
                        Assert.assertEquals(emptyTypes[index], message.getType());
                        return;
                    }
                }
                Assert.fail("Unknown semantic control vector: " + id);
        }
    }

    private static boolean interopRequired() {
        return "1".equals(System.getenv("SC_DEVICE_PROTOCOL_INTEROP_REQUIRED"));
    }

    @Test
    public void actualReaderConsumesStaticAndNativeProducedControlFrames() throws IOException {
        Map<String, Frame> expected = goldens("control");
        Assert.assertEquals(CONTROL_CASES, expected.size());
        String nativeFrames = System.getenv("SC_DEVICE_PROTOCOL_C_CONTROL");
        Map<String, Frame> observed = expected;

        if (nativeFrames != null) {
            observed = new LinkedHashMap<>();

            for (String[] row : rows(Paths.get(nativeFrames))) {
                Assert.assertEquals(2, row.length);
                Frame frame = new Frame(row[0], hex(row[1]), 0);
                Assert.assertNull(observed.put(frame.id, frame));
            }
            Assert.assertEquals(expected.keySet(), observed.keySet());
        } else {
            Assert.assertFalse("Required C-produced frames are missing", interopRequired());
        }

        for (Frame frame : observed.values()) {
            Assert.assertArrayEquals(frame.id, expected.get(frame.id).bytes, frame.bytes);
            assertControl(frame.id, new ControlMessageReader(new ByteArrayInputStream(frame.bytes)).read());
        }
        System.out.println((nativeFrames == null ? "Static golden -> Java: " : "Actual C -> Java: ") + observed.size() + " frames");
    }

    private static void assertTruncated(byte[] bytes, int length) throws IOException {

        try {
            new ControlMessageReader(new ByteArrayInputStream(Arrays.copyOf(bytes, length))).read();
            Assert.fail("Truncated frame was accepted at " + length);
        } catch (EOFException expected) {
            // A finite prefix is incomplete, not a malformed declaration.
        }
    }

    @Test
    public void finitePrefixesRemainIncompleteWithoutLargeQuadraticCopies() throws IOException {
        for (Frame frame : goldens("control").values()) {

            if (frame.bytes.length < 4096) {

                for (int length = 0; length < frame.bytes.length; ++length) {
                    assertTruncated(frame.bytes, length);
                }
            } else {
                int[] prefixes = {0, 1, frame.prefixLength - 1, frame.prefixLength, frame.bytes.length - 1};

                for (int length : prefixes) {
                    assertTruncated(frame.bytes, length);
                }
            }
        }
    }

    @Test
    public void concatenatedControlMessagesKeepTheirBoundaries() throws IOException {
        Map<String, Frame> frames = goldens("control");
        ByteArrayOutputStream stream = new ByteArrayOutputStream();

        for (Frame frame : frames.values()) {
            stream.write(frame.bytes);
        }
        ControlMessageReader reader = new ControlMessageReader(new ByteArrayInputStream(stream.toByteArray()));

        for (Frame frame : frames.values()) {
            assertControl(frame.id, reader.read());
        }

        try {
            reader.read();
            Assert.fail("Expected end of the concatenated finite stream");
        } catch (EOFException expected) {
            // No byte of the next frame was consumed as part of its predecessor.
        }
    }

    private static String repeatedText(int length) {
        char[] value = new char[length];
        Arrays.fill(value, 'x');
        return new String(value);
    }

    private static DeviceMessage deviceMessage(String id) {
        switch (id) {
            case "clipboard":
                return DeviceMessage.createClipboard("A\u20ac");
            case "clipboard-empty":
                return DeviceMessage.createClipboard("");
            case "clipboard-max":
                return DeviceMessage.createClipboard(repeatedText(262139));
            case "clipboard-utf8-cut":
                return DeviceMessage.createClipboard(repeatedText(262138) + "\u20ac");
            case "ack":
                return DeviceMessage.createAckClipboard(SEQUENCE_BITS);
            case "ack-zero":
                return DeviceMessage.createAckClipboard(0);
            case "ack-max":
                return DeviceMessage.createAckClipboard(-1L);
            case "uhid-output":
                return DeviceMessage.createUhidOutput(0x1234, new byte[] {0, -1, -128});
            case "uhid-output-empty":
                return DeviceMessage.createUhidOutput(0x1234, new byte[0]);
            case "uhid-output-max":
                byte[] data = new byte[65535];
                Arrays.fill(data, (byte) 0xa5);
                return DeviceMessage.createUhidOutput(65535, data);
            default:
                throw new AssertionError("Unknown device semantic vector: " + id);
        }
    }

    @Test
    public void actualWriterMatchesGoldensAndEmitsRequestedReverseFrames() throws IOException {
        Map<String, Frame> expected = goldens("device");
        Assert.assertEquals(DEVICE_CASES, expected.size());
        List<String> emitted = new ArrayList<>();

        for (Frame frame : expected.values()) {
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            new DeviceMessageWriter(output).write(deviceMessage(frame.id));
            byte[] actual = output.toByteArray();
            Assert.assertArrayEquals(frame.id, frame.bytes, actual);
            emitted.add(frame.id + "\t" + hex(actual));
        }
        String destination = System.getenv("SC_DEVICE_PROTOCOL_JAVA_DEVICE");

        if (destination != null) {
            Files.write(Paths.get(destination), emitted, StandardCharsets.UTF_8, StandardOpenOption.CREATE_NEW);
        } else {
            Assert.assertFalse("Required Java-produced artifact path is missing", interopRequired());
        }
        System.out.println("Actual Java writer: " + emitted.size() + " frames" + (destination == null ? " (static golden check)" : " for C"));
    }

    @Test
    public void sharedUnicodeScalarCasesUseTheActualTextReaderAndWriter() throws IOException {
        int cases = 0;

        for (String[] row : rows(specification().resolve("utf8.tsv"))) {
            Assert.assertEquals(3, row.length);
            boolean accepted = row[0].equals("accept");
            Assert.assertTrue(accepted || row[0].equals("reject"));
            byte[] payload = row[2].equals("-") ? new byte[0] : hex(row[2]);
            ByteArrayOutputStream frame = new ByteArrayOutputStream();
            DataOutputStream data = new DataOutputStream(frame);
            data.writeByte(1);
            data.writeInt(payload.length);
            data.write(payload);

            try {
                ControlMessage message = new ControlMessageReader(new ByteArrayInputStream(frame.toByteArray())).read();
                Assert.assertTrue("Rejected scalar case was accepted: " + row[1], accepted);
                Assert.assertArrayEquals(payload, message.getText().getBytes(StandardCharsets.UTF_8));
                ByteArrayOutputStream device = new ByteArrayOutputStream();
                new DeviceMessageWriter(device).write(DeviceMessage.createClipboard(message.getText()));
                byte[] expected = frame.toByteArray();
                expected[0] = 0;
                Assert.assertArrayEquals(expected, device.toByteArray());
            } catch (ControlProtocolException rejected) {
                Assert.assertFalse("Valid scalar case was rejected: " + row[1], accepted);
            }
            ++cases;
        }
        Assert.assertEquals(30, cases);
        System.out.println("Java text profile: " + cases + " shared scalar cases");
    }
}
