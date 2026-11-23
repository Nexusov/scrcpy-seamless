package com.genymobile.scrcpy.control;

import org.junit.Assert;
import org.junit.Test;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;

public class DeviceProtocolBoundsTest {
    @Test(expected = ControlProtocolException.class)
    public void negativeUnsignedTextLengthIsRejectedBeforeAllocation() throws IOException {
        new ControlMessageReader(new ByteArrayInputStream(new byte[] {1, -1, -1, -1, -1})).read();
    }

    @Test(expected = ControlProtocolException.class)
    public void oversizedInjectDeclarationIsRejectedBeforeWaitingForItsBody() throws IOException {
        new ControlMessageReader(new ByteArrayInputStream(new byte[] {1, 0, 0, 1, 45})).read();
    }

    @Test(expected = ControlProtocolException.class)
    public void oversizedClipboardDeclarationIsRejectedBeforeWaitingForItsBody() throws IOException {
        byte[] declaration = {9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, -1, -13};
        new ControlMessageReader(new ByteArrayInputStream(declaration)).read();
    }

    @Test(expected = ControlProtocolException.class)
    public void unknownControlTypeRemainsAProtocolFailure() throws IOException {
        new ControlMessageReader(new ByteArrayInputStream(new byte[] {-1})).read();
    }

    private static void assertRejectedWithoutEmission(DeviceMessage message) throws IOException {
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        DeviceMessageWriter writer = new DeviceMessageWriter(output);

        try {
            writer.write(message);
            Assert.fail("Unrepresentable message was accepted");
        } catch (ControlProtocolException expected) {
            Assert.assertEquals(0, output.size());
        }
        writer.write(DeviceMessage.createAckClipboard(0x0102030405060708L));
        Assert.assertArrayEquals(new byte[] {1, 1, 2, 3, 4, 5, 6, 7, 8}, output.toByteArray());
    }

    @Test
    public void negativeUhidIdCannotWrapIntoAnUnsignedField() throws IOException {
        assertRejectedWithoutEmission(DeviceMessage.createUhidOutput(-1, new byte[] {1}));
    }

    @Test
    public void oversizedUhidIdCannotWrapIntoAnUnsignedField() throws IOException {
        assertRejectedWithoutEmission(DeviceMessage.createUhidOutput(65536, new byte[] {1}));
    }

    @Test
    public void oversizedUhidDataCannotCorruptTheNextFrame() throws IOException {
        assertRejectedWithoutEmission(DeviceMessage.createUhidOutput(42, new byte[65536]));
    }

    @Test
    public void absentUhidPayloadDoesNotLeaveAnUnflushedPartialHeader() throws IOException {
        assertRejectedWithoutEmission(DeviceMessage.createUhidOutput(42, null));
    }
}
