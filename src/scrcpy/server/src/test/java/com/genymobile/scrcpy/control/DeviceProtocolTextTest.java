package com.genymobile.scrcpy.control;

import org.junit.Assert;
import org.junit.Test;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;

public class DeviceProtocolTextTest {
    @Test(expected = ControlProtocolException.class)
    public void readerRejectsMalformedUtf8InsteadOfReplacingIt() throws IOException {
        new ControlMessageReader(new ByteArrayInputStream(new byte[] {1, 0, 0, 0, 1, -1})).read();
    }

    @Test(expected = ControlProtocolException.class)
    public void readerRejectsEmbeddedNulText() throws IOException {
        new ControlMessageReader(new ByteArrayInputStream(new byte[] {1, 0, 0, 0, 3, 65, 0, 66})).read();
    }

    @Test(expected = ControlProtocolException.class)
    public void tinyNamesUseTheSameStrictTextPolicy() throws IOException {
        new ControlMessageReader(new ByteArrayInputStream(new byte[] {16, 1, -1})).read();
    }

    private static void assertRejected(String text) throws IOException {
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        DeviceMessageWriter writer = new DeviceMessageWriter(output);

        try {
            writer.write(DeviceMessage.createClipboard(text));
            Assert.fail("Unsupported text was emitted");
        } catch (ControlProtocolException expected) {
            Assert.assertEquals(0, output.size());
        }
        writer.write(DeviceMessage.createAckClipboard(1));
        Assert.assertArrayEquals(new byte[] {1, 0, 0, 0, 0, 0, 0, 0, 1}, output.toByteArray());
    }

    @Test
    public void writerRejectsNulBeforeAnyHeader() throws IOException {
        assertRejected("A\u0000B");
    }

    @Test
    public void writerRejectsUnpairedSurrogateBeforeAnyHeader() throws IOException {
        assertRejected("\ud800");
    }

    @Test
    public void invalidSourceTailIsNotHiddenByWireTruncation() throws IOException {
        char[] source = new char[262141];
        java.util.Arrays.fill(source, 'x');
        source[source.length - 1] = '\ud800';
        assertRejected(new String(source));
    }

    @Test
    public void blankClipboardAndBinaryUhidRemainValid() throws IOException {
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        DeviceMessageWriter writer = new DeviceMessageWriter(output);
        writer.write(DeviceMessage.createClipboard(""));
        writer.write(DeviceMessage.createUhidOutput(1, new byte[] {0, -1, -128}));
        Assert.assertArrayEquals(new byte[] {0, 0, 0, 0, 0, 2, 0, 1, 0, 3, 0, -1, -128}, output.toByteArray());
    }
}
