package com.genymobile.scrcpy.video;

import android.hardware.camera2.CameraAccessException;

/** Isolate the framework stop effect; the camera thread still owns its fields. */
final class CameraRequestStop {
    private CameraRequestStop() {
    }

    interface Request {
        void stop() throws CameraAccessException;
    }

    static Exception stop(Request request) {

        if (request == null) {
            return null;
        }

        try {
            request.stop();
        } catch (CameraAccessException | IllegalStateException failure) {
            // A disconnected or already closed framework session may reject Stop.
            return failure;
        }
        return null;
    }
}
