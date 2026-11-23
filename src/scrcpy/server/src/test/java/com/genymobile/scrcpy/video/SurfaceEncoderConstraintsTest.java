package com.genymobile.scrcpy.video;

import com.genymobile.scrcpy.BuildConfig;
import com.genymobile.scrcpy.Options;
import com.genymobile.scrcpy.model.Size;
import com.genymobile.scrcpy.util.Ln;

import android.view.Surface;
import org.junit.After;
import org.junit.Assert;
import org.junit.Before;
import org.junit.Test;

import java.lang.reflect.Field;
import java.util.ArrayList;
import java.util.List;

public class SurfaceEncoderConstraintsTest {
    private static final VideoSizeCapabilities CAPABILITIES = new FakeVideoSizeCapabilities(16, 4096, 16, 4096, 512L * 512);
    private Ln.Level previousLogLevel;

    private static final class Capture extends SurfaceCapture {
        private final List<VideoConstraints> applied = new ArrayList<>();
        private int acceptAfter = 1;

        @Override
        protected void init(VideoConstraints constraints) {
            Assert.fail("No Android capture initialization is expected");
        }

        @Override
        public void release() {
            Assert.fail("No Android capture acquisition is expected");
        }

        @Override
        public void start(Surface surface) {
            Assert.fail("No device capture is expected");
        }

        @Override
        public Size getSize() {
            return new Size(3000, 2000);
        }

        @Override
        protected boolean applyNewVideoConstraints(VideoConstraints constraints) {
            applied.add(constraints);
            return applied.size() >= acceptAfter;
        }
    }

    private static final class Encoder extends SurfaceEncoder {
        private int waits;

        Encoder(Capture capture, boolean downsize) {
            super(capture, null, Options.parse(BuildConfig.VERSION_NAME, "downsize_on_error=" + downsize));
        }

        @Override
        void waitBeforeRetry() {
            ++waits;
        }
    }

    @Before
    public void suppressAndroidLoggingEffectAndRetainItsPriorLevel() {
        for (Ln.Level level : Ln.Level.values()) {

            if (Ln.isEnabled(level)) {
                previousLogLevel = level;
                break;
            }
        }
        Assert.assertNotNull(previousLogLevel);
        Ln.initLogLevel(Ln.Level.ERROR);
    }

    @After
    public void restoreLoggingLevel() {
        Ln.initLogLevel(previousLogLevel);
    }

    private static void setState(Encoder encoder, String name, Object value) throws ReflectiveOperationException {
        Field field = SurfaceEncoder.class.getDeclaredField(name);
        field.setAccessible(true);
        field.set(encoder, value);
    }

    private static Encoder encoder(Capture capture, boolean downsize) throws ReflectiveOperationException {
        Encoder encoder = new Encoder(capture, downsize);
        setState(encoder, "videoConstraints", SurfaceEncoder.createVideoConstraints(0, 8, CAPABILITIES));
        return encoder;
    }

    @Test
    public void keepsDeclaredCapabilitiesOutOfTheInitialAttempt() {
        VideoConstraints initial = SurfaceEncoder.createVideoConstraints(0, 8, CAPABILITIES);
        Assert.assertNull(initial.getEncoderCapabilities());
        Assert.assertEquals(8, initial.getAlignment());
    }

    @Test
    public void firstFailureAppliesCapabilitiesBeforeAnyDownsize() throws ReflectiveOperationException {
        Capture capture = new Capture();
        Encoder encoder = encoder(capture, true);
        Assert.assertTrue(encoder.prepareRetry(CAPABILITIES, new Size(800, 600)));
        Assert.assertEquals(1, capture.applied.size());
        Assert.assertSame(CAPABILITIES, capture.applied.get(0).getEncoderCapabilities());
        Assert.assertEquals(0, capture.applied.get(0).getMaxSize());
        Assert.assertEquals(0, encoder.waits);
    }

    @Test
    public void disabledDownsizingBeforeFirstFrameDoesNotApplyCapabilities() throws ReflectiveOperationException {
        Capture capture = new Capture();
        Encoder encoder = encoder(capture, false);
        Assert.assertFalse(encoder.prepareRetry(CAPABILITIES, new Size(3000, 2000)));
        Assert.assertTrue(capture.applied.isEmpty());
        Assert.assertEquals(0, encoder.waits);
    }

    @Test
    public void rejectedCapabilitiesMayUseTheExistingSmallerFallbackOnce() throws ReflectiveOperationException {
        Capture capture = new Capture();
        capture.acceptAfter = 2;
        Encoder encoder = encoder(capture, true);
        Assert.assertTrue(encoder.prepareRetry(CAPABILITIES, new Size(3000, 2000)));
        Assert.assertEquals(2, capture.applied.size());
        Assert.assertEquals(0, capture.applied.get(0).getMaxSize());
        Assert.assertEquals(2560, capture.applied.get(1).getMaxSize());
        Assert.assertSame(CAPABILITIES, capture.applied.get(1).getEncoderCapabilities());
        Assert.assertTrue(encoder.prepareRetry(CAPABILITIES, new Size(2560, 1800)));
        Assert.assertEquals(3, capture.applied.size());
        Assert.assertEquals(1920, capture.applied.get(2).getMaxSize());
    }

    @Test
    public void rejectedFallbackOrExhaustedSizeRemainsFailure() throws ReflectiveOperationException {
        Capture capture = new Capture();
        capture.acceptAfter = Integer.MAX_VALUE;
        Encoder encoder = encoder(capture, true);
        Assert.assertFalse(encoder.prepareRetry(CAPABILITIES, new Size(3000, 2000)));
        Assert.assertEquals(2, capture.applied.size());
        Assert.assertFalse(encoder.prepareRetry(CAPABILITIES, new Size(800, 600)));
        Assert.assertEquals(2, capture.applied.size());
    }

    @Test
    public void activeStreamKeepsTwoWaitsThenOneUntriedCapabilityUpdate() throws ReflectiveOperationException {
        Capture capture = new Capture();
        Encoder encoder = encoder(capture, true);
        setState(encoder, "firstFrameSent", true);
        Assert.assertTrue(encoder.prepareRetry(CAPABILITIES, new Size(3000, 2000)));
        Assert.assertTrue(encoder.prepareRetry(CAPABILITIES, new Size(3000, 2000)));
        Assert.assertEquals(2, encoder.waits);
        Assert.assertTrue(capture.applied.isEmpty());
        Assert.assertTrue(encoder.prepareRetry(CAPABILITIES, new Size(3000, 2000)));
        Assert.assertEquals(1, capture.applied.size());
        Assert.assertFalse(encoder.prepareRetry(CAPABILITIES, new Size(3000, 2000)));
        Assert.assertEquals(1, capture.applied.size());
        Assert.assertEquals(2, encoder.waits);
    }

    @Test
    public void activeStreamOptOutStillKeepsTheOriginalRetryLimit() throws ReflectiveOperationException {
        Capture capture = new Capture();
        Encoder encoder = encoder(capture, false);
        setState(encoder, "firstFrameSent", true);
        Assert.assertTrue(encoder.prepareRetry(CAPABILITIES, new Size(3000, 2000)));
        Assert.assertTrue(encoder.prepareRetry(CAPABILITIES, new Size(3000, 2000)));
        Assert.assertFalse(encoder.prepareRetry(CAPABILITIES, new Size(3000, 2000)));
        Assert.assertEquals(2, encoder.waits);
        Assert.assertTrue(capture.applied.isEmpty());
    }
}
