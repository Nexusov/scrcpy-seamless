package com.genymobile.scrcpy.model;

import com.genymobile.scrcpy.video.FakeVideoSizeCapabilities;
import com.genymobile.scrcpy.video.VideoConstraints;

import org.junit.Assert;
import org.junit.Test;

public class SizeTest {

    private VideoConstraints createVideoConstraints(int maxSize, int alignment) {
        Size maxCodecSize = new Size(4096, 4096);
        return createVideoConstraints(maxSize, alignment, maxCodecSize);
    }

    private VideoConstraints createVideoConstraints(int maxSize, int alignment, Size maxCodecSize) {
        return createVideoConstraints(maxSize, alignment, maxCodecSize, 1);
    }

    private VideoConstraints createVideoConstraints(int maxSize, int alignment, Size maxCodecSize, int minCodecSize) {
        return new VideoConstraints(maxSize, alignment, new FakeVideoSizeCapabilities(minCodecSize, maxCodecSize.getWidth(),
                minCodecSize, maxCodecSize.getHeight(), Long.MAX_VALUE));
    }

    @Test
    public void testConstrainSize() {
        Size size = new Size(207, 209);

        Assert.assertEquals(size, size.constrain(createVideoConstraints(0, 1)));
        Assert.assertEquals(new Size(206, 208), size.constrain(createVideoConstraints(0, 2)));
        Assert.assertEquals(new Size(204, 208), size.constrain(createVideoConstraints(0, 4)));

        Size s = size.constrain(createVideoConstraints(208, 2));
        Assert.assertEquals(208, s.getHeight());
        Assert.assertTrue(s.getWidth() >= 206 && s.getWidth() <= 208 && s.getWidth() % 2 == 0);

        Assert.assertEquals(new Size(207 * 208 / 209, 208), size.constrain(createVideoConstraints(208, 2)));
        Assert.assertEquals(new Size(204, 208), size.constrain(createVideoConstraints(208, 4)));
    }

    @Test
    public void testConstrainSizeUnchanged() {
        Size size = new Size(256, 512);

        Assert.assertEquals(size, size.constrain(createVideoConstraints(0, 1)));
        Assert.assertEquals(size, size.constrain(createVideoConstraints(512, 1)));
        Assert.assertEquals(size, size.constrain(createVideoConstraints(515, 1)));
        Assert.assertEquals(size, size.constrain(createVideoConstraints(512, 16)));
        Assert.assertEquals(size, size.constrain(createVideoConstraints(515, 16)));
        Assert.assertEquals(size, size.constrain(createVideoConstraints(0, 16)));
        Assert.assertEquals(size, size.constrain(createVideoConstraints(4096, 16)));
    }

    @Test
    public void testConstrainMaxCodecSize() {
        Size maxCodecSize = new Size(400, 600);

        Size size = new Size(314, 617);
        Assert.assertEquals(new Size(314 * 600 / 617, 600), size.constrain(createVideoConstraints(0, 1, maxCodecSize)));
        Assert.assertEquals(new Size(314 * 550 / 617, 550), size.constrain(createVideoConstraints(550, 1, maxCodecSize)));

        size = new Size(512, 536);
        // 419 is the highest major dimension whose scaled width still floors to 400.
        Assert.assertEquals(new Size(400, 419), size.constrain(createVideoConstraints(0, 1, maxCodecSize)));
        Assert.assertEquals(new Size(400, 419), size.constrain(createVideoConstraints(550, 1, maxCodecSize)));
        Assert.assertEquals(new Size(400, 419), size.constrain(createVideoConstraints(500, 1, maxCodecSize)));
        Assert.assertEquals(new Size(512 * 410 / 536, 410), size.constrain(createVideoConstraints(410, 1, maxCodecSize)));
    }

    @Test
    public void testConstrainMinCodecSize() {
        Size maxCodecSize = new Size(1024, 1024);

        Size size = new Size(800, 600);
        Assert.assertEquals(new Size(800, 600), size.constrain(createVideoConstraints(800, 1, maxCodecSize, 512)));
        Assert.assertEquals(new Size(512, 512), size.constrain(createVideoConstraints(400, 1, maxCodecSize, 512)));
    }

    @Test
    public void coupledCapabilitiesDoNotReduceEveryAspectToTheExtremeRectangle() {
        VideoConstraints constraints = new VideoConstraints(0, 16, new FakeVideoSizeCapabilities(16, 1024, 16, 1024, 512L * 512));
        Assert.assertEquals(new Size(512, 512), new Size(1000, 1000).constrain(constraints));
    }

    @Test
    public void minimumMinorDimensionDoesNotHideTheValidSearchInterval() {
        VideoConstraints constraints = new VideoConstraints(0, 4, new FakeVideoSizeCapabilities(64, 1024, 64, 1024, 70000));
        Assert.assertEquals(new Size(80, 836), new Size(100, 1000).constrain(constraints));
    }

    @Test
    public void scaledDimensionsDoNotOverflowIntegerProducts() {
        VideoConstraints constraints = new VideoConstraints(0, 16, new FakeVideoSizeCapabilities(16, 100000, 16, 100000, 10000000000L));
        Assert.assertEquals(new Size(100000, 100000), new Size(100000, 100000).constrain(constraints));
    }

    @Test
    public void minimumBlockAreaDoesNotHideTheValidSearchInterval() {
        VideoConstraints constraints = new VideoConstraints(0, 4, new FakeVideoSizeCapabilities(80, 1024, 80, 1024, 80000, 82000));
        Assert.assertEquals(new Size(88, 916), new Size(100, 1000).constrain(constraints));
    }

    @Test
    public void freeAspectClampsCodecMinimaBeforeDependentQueries() {
        VideoConstraints constraints = createVideoConstraints(400, 1, new Size(1024, 1024), 512);
        Assert.assertEquals(new Size(512, 512), new Size(800, 600).constrain(constraints, false));
    }

    @Test
    public void capabilitySearchMatchesAnIndependentExhaustiveNumericOracle() {
        int checked = 0;

        for (int maxArea : new int[] {256, 512, 1024}) {
            for (int minArea : new int[] {0, 200}) {
                FakeVideoSizeCapabilities caps = new FakeVideoSizeCapabilities(8, 32, 8, 32, minArea, maxArea);

                for (int alignment : new int[] {1, 2, 4}) {
                    for (int width = 8; width <= 56; width += 3) {
                        for (int height = 8; height <= 56; height += 5) {
                            int major = Math.max(width, height);
                            Size expected = null;

                            for (int pixels = major / alignment * alignment; pixels > 0; pixels -= alignment) {
                                int w = (int) ((long) width * pixels / major) / alignment * alignment;
                                int h = (int) ((long) height * pixels / major) / alignment * alignment;

                                if (caps.isSizeSupported(w, h)) {
                                    expected = new Size(w, h);
                                    break;
                                }
                            }

                            if (expected != null) {
                                VideoConstraints constraints = new VideoConstraints(0, alignment, caps);
                                Assert.assertEquals("input=" + width + "x" + height + ", alignment=" + alignment
                                        + ", area=" + minArea + ".." + maxArea, expected, new Size(width, height).constrain(constraints));
                                ++checked;
                            }
                        }
                    }
                }
            }
        }
        Assert.assertTrue(checked > 100);
        System.out.println("Size constraints: " + checked + " independently enumerated valid shapes");
    }

    @Test
    public void unsupportedSdkQueryKeepsTheExplicitValidationFallback() {
        int[] queries = {0};
        FakeVideoSizeCapabilities caps = new FakeVideoSizeCapabilities(1, 1024, 1, 1024, Long.MAX_VALUE) {
            @Override
            public int getMaxHeightForWidth(int width) {
                ++queries[0];
                throw new IllegalArgumentException("synthetic unsupported SDK width");
            }
        };
        Assert.assertEquals(new Size(100, 100), new Size(100, 100).constrain(new VideoConstraints(0, 1, caps)));
        Assert.assertTrue(queries[0] > 0);
    }

    @Test(expected = IllegalStateException.class)
    public void unexpectedSdkFailureRemainsVisible() {
        FakeVideoSizeCapabilities caps = new FakeVideoSizeCapabilities(1, 1024, 1, 1024, Long.MAX_VALUE) {
            @Override
            public int getMaxHeightForWidth(int width) {
                throw new IllegalStateException("synthetic unexpected SDK failure");
            }
        };
        new Size(100, 100).constrain(new VideoConstraints(0, 1, caps));
    }
}
