package com.genymobile.scrcpy.video;

import org.junit.Assert;
import org.junit.Test;

public class CameraRequestStopTest {
    @Test
    public void invokesTheExistingSessionExactlyOnce() {
        int[] requests = {0};
        Exception failure = CameraRequestStop.stop(() -> ++requests[0]);
        Assert.assertNull(failure);
        Assert.assertEquals(1, requests[0]);
    }

    @Test
    public void absentSessionRequiresNoStop() {
        Assert.assertNull(CameraRequestStop.stop(null));
    }

    @Test
    public void alreadyClosedSessionIsReportedWithoutBlockingOwnerRelease() {
        IllegalStateException closed = new IllegalStateException("synthetic closed session");
        Exception failure = CameraRequestStop.stop(() -> {
            throw closed;
        });
        Assert.assertSame(closed, failure);
    }

    @Test(expected = IllegalArgumentException.class)
    public void unexpectedFailureIsNotSilentlyConvertedToSettlement() {
        CameraRequestStop.stop(() -> {
            throw new IllegalArgumentException("synthetic unexpected failure");
        });
    }
}
