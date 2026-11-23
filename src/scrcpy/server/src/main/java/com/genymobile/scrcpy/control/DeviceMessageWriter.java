package com.genymobile.scrcpy.control;

import java.io.BufferedOutputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.io.OutputStream;

public class DeviceMessageWriter {

    private static final int MESSAGE_MAX_SIZE = 1 << 18; // 256k
    private static final int UNSIGNED_SHORT_MAX = 0xffff;
    public static final int CLIPBOARD_TEXT_MAX_LENGTH = MESSAGE_MAX_SIZE - 5; // type: 1 byte; length: 4 bytes

    private final DataOutputStream dos;

    public DeviceMessageWriter(OutputStream rawOutputStream) {
        dos = new DataOutputStream(new BufferedOutputStream(rawOutputStream));
    }

    public void write(DeviceMessage msg) throws IOException {
        int type = msg.getType();

        if (type != DeviceMessage.TYPE_CLIPBOARD && type != DeviceMessage.TYPE_ACK_CLIPBOARD && type != DeviceMessage.TYPE_UHID_OUTPUT) {
            throw new ControlProtocolException("Unknown event type: " + type);
        }
        byte[] clipboard = type == DeviceMessage.TYPE_CLIPBOARD ? DeviceProtocolText.encode(msg.getText(), CLIPBOARD_TEXT_MAX_LENGTH) : null;

        if (type == DeviceMessage.TYPE_UHID_OUTPUT) {
            byte[] payload = msg.getData();

            if (msg.getId() < 0 || msg.getId() > UNSIGNED_SHORT_MAX || payload == null || payload.length > UNSIGNED_SHORT_MAX) {
                // Reject before touching the buffered stream, including its type byte.
                throw new ControlProtocolException("Unrepresentable UHID output");
            }
        }
        dos.writeByte(type);
        switch (type) {
            case DeviceMessage.TYPE_CLIPBOARD:
                dos.writeInt(clipboard.length);
                dos.write(clipboard);
                break;
            case DeviceMessage.TYPE_ACK_CLIPBOARD:
                dos.writeLong(msg.getSequence());
                break;
            case DeviceMessage.TYPE_UHID_OUTPUT:
                dos.writeShort(msg.getId());
                byte[] data = msg.getData();
                dos.writeShort(data.length);
                dos.write(data);
                break;
            default:
                throw new ControlProtocolException("Unknown event type: " + type);
        }
        dos.flush();
    }
}
