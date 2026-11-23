package com.genymobile.scrcpy.video;

/** Independent numeric codec shape: ranges plus an optional coupled area bound. */
public class FakeVideoSizeCapabilities implements VideoSizeCapabilities {
    private final int minWidth;
    private final int maxWidth;
    private final int minHeight;
    private final int maxHeight;
    private final long maxArea;
    private final long minArea;

    public FakeVideoSizeCapabilities(int minWidth, int maxWidth, int minHeight, int maxHeight, long maxArea) {
        this(minWidth, maxWidth, minHeight, maxHeight, 0, maxArea);
    }

    public FakeVideoSizeCapabilities(int minWidth, int maxWidth, int minHeight, int maxHeight, long minArea, long maxArea) {
        this.minWidth = minWidth;
        this.maxWidth = maxWidth;
        this.minHeight = minHeight;
        this.maxHeight = maxHeight;
        this.maxArea = maxArea;
        this.minArea = minArea;
    }

    @Override
    public int getWidthAlignment() {
        return 2;
    }

    @Override
    public int getHeightAlignment() {
        return 4;
    }

    @Override
    public int getMinWidth() {
        return minWidth;
    }

    @Override
    public int getMaxWidth() {
        return maxWidth;
    }

    @Override
    public int getMinHeight() {
        return minHeight;
    }

    @Override
    public int getMaxHeight() {
        return maxHeight;
    }

    @Override
    public int getMaxWidthForHeight(int height) {

        if (height < minHeight || height > maxHeight) {
            throw new IllegalArgumentException("unsupported synthetic height");
        }
        return (int) Math.min(maxWidth, maxArea / height);
    }

    @Override
    public int getMaxHeightForWidth(int width) {

        if (width < minWidth || width > maxWidth) {
            throw new IllegalArgumentException("unsupported synthetic width");
        }
        return (int) Math.min(maxHeight, maxArea / width);
    }

    @Override
    public boolean isSizeSupported(int width, int height) {
        long area = (long) width * height;
        return width >= minWidth && width <= maxWidth && height >= minHeight && height <= maxHeight && area >= minArea && area <= maxArea;
    }
}
