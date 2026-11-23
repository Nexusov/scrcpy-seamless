package com.genymobile.scrcpy.video;

import com.genymobile.scrcpy.BuildConfig;
import com.genymobile.scrcpy.Options;
import com.genymobile.scrcpy.display.DisplayInfo;
import com.genymobile.scrcpy.model.Orientation;
import com.genymobile.scrcpy.model.Size;
import com.genymobile.scrcpy.util.AffineMatrix;

import org.junit.Assert;
import org.junit.Test;

import java.lang.reflect.Field;

public class CaptureGeometryTest {
    private static void setState(NewDisplayCapture capture, String name, Object value) throws ReflectiveOperationException {
        Field field = NewDisplayCapture.class.getDeclaredField(name);
        field.setAccessible(true);
        field.set(capture, value);
    }

    private static NewDisplayCapture display(VideoConstraints constraints) throws ReflectiveOperationException {
        Options options = Options.parse(BuildConfig.VERSION_NAME, "new_display=800x600/320");
        NewDisplayCapture capture = new NewDisplayCapture(null, options);
        setState(capture, "videoConstraints", constraints);
        setState(capture, "dpi", 320);
        return capture;
    }

    @Test
    public void cameraOutputUsesOnlyAlignmentAfterSelectingItsCaptureSize() {
        VideoConstraints constraints = new VideoConstraints(100, 16, new FakeVideoSizeCapabilities(512, 1024, 512, 1024, Long.MAX_VALUE));
        Assert.assertEquals(new Size(640, 480), CameraCapture.alignVideoSize(new Size(641, 481), constraints));
    }

    @Test
    public void cameraSizeChangeRequiresAnIntermediateIdentityTransform() {
        Assert.assertSame(AffineMatrix.IDENTITY, CameraCapture.adaptVideoTransform(new Size(641, 481), new Size(640, 480), null));
    }

    @Test
    public void cameraRetainsExistingTransformAndTheDirectSameSizePath() {
        AffineMatrix transform = new AffineMatrix(1, 0, 0, 1, 3, 4);
        Assert.assertSame(transform, CameraCapture.adaptVideoTransform(new Size(641, 481), new Size(640, 480), transform));
        Assert.assertNull(CameraCapture.adaptVideoTransform(new Size(640, 480), new Size(640, 480), null));
    }

    @Test
    public void virtualDisplayResizeMayChangeAspectRatio() throws ReflectiveOperationException {
        VideoConstraints constraints = new VideoConstraints(0, 1, new FakeVideoSizeCapabilities(1, 400, 1, 600, Long.MAX_VALUE));
        NewDisplayCapture capture = display(constraints);
        DisplayInfo info = new DisplayInfo(7, new Size(800, 600), 0, 0, 0, 240, "synthetic");
        NewDisplayCapture.ResizeTarget target = capture.prepareResize(new Size(800, 600), info);
        Assert.assertEquals(new Size(400, 600), target.getLogical().getSize());
        Assert.assertEquals(new Size(400, 600), target.getPhysical());
    }

    @Test
    public void virtualDisplayResizeUsesCurrentDisplayDpi() throws ReflectiveOperationException {
        NewDisplayCapture capture = display(new VideoConstraints(0, 1, null));
        DisplayInfo info = new DisplayInfo(7, new Size(800, 600), 0, 0, 0, 240, "synthetic");
        Assert.assertEquals(240, capture.prepareResize(new Size(800, 600), info).getDpi());
    }

    @Test
    public void logicalAndPhysicalRotationRemainSeparate() throws ReflectiveOperationException {
        NewDisplayCapture capture = display(new VideoConstraints(0, 1, null));

        for (Orientation orientation : Orientation.values()) {
            setState(capture, "captureOrientation", orientation);

            for (int rotation = 0; rotation < 4; ++rotation) {
                DisplayInfo info = new DisplayInfo(7, new Size(800, 600), rotation, 0, 0, 240, "synthetic");
                NewDisplayCapture.ResizeTarget target = capture.prepareResize(new Size(800, 600), info);
                Size logical = orientation.isSwap() ? new Size(600, 800) : new Size(800, 600);
                Size physical = (rotation % 2) == 0 ? logical : logical.rotate();
                Assert.assertEquals(logical, target.getLogical().getSize());
                Assert.assertEquals(rotation, target.getLogical().getRotation());
                Assert.assertEquals(physical, target.getPhysical());
            }
        }
    }
}
