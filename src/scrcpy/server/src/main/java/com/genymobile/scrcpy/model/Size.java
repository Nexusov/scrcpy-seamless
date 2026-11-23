package com.genymobile.scrcpy.model;

import com.genymobile.scrcpy.util.BinarySearch;
import com.genymobile.scrcpy.util.Ln;
import com.genymobile.scrcpy.video.VideoConstraints;
import com.genymobile.scrcpy.video.VideoSizeCapabilities;

import android.graphics.Rect;

import java.util.Objects;

public final class Size {
    private final int width;
    private final int height;

    public Size(int width, int height) {
        this.width = width;
        this.height = height;
    }

    public int getWidth() {
        return width;
    }

    public int getHeight() {
        return height;
    }

    public int getMax() {
        return Math.max(width, height);
    }

    public Size rotate() {
        return new Size(height, width);
    }

    public Size constrain(VideoConstraints constraints) {
        return constrain(constraints, true);
    }

    public Size constrain(VideoConstraints constraints, boolean preserveAspectRatio) {
        int maxSize = constraints.getMaxSize();
        int alignment = constraints.getAlignment();
        VideoSizeCapabilities caps = constraints.getEncoderCapabilities();

        assert maxSize >= 0 : "Max size may not be negative";
        assert width > 0 && height > 0 : "Video dimensions must be positive";
        assert alignment > 0 : "Alignment must be positive";
        assert (alignment & (alignment - 1)) == 0 : "Alignment must be a power-of-two";

        if (caps == null) {
            int w, h;

            if (maxSize > 0 && (width > maxSize || height > maxSize)) {

                if (preserveAspectRatio) {

                    if (width > height) {
                        w = maxSize;
                        h = (int) ((long) height * maxSize / width);
                    } else {
                        w = (int) ((long) width * maxSize / height);
                        h = maxSize;
                    }
                } else {
                    w = Math.min(width, maxSize);
                    h = Math.min(height, maxSize);
                }
            } else {
                // No constraints
                w = width;
                h = height;
            }

            w = Math.max(align(w, alignment), alignment);
            h = Math.max(align(h, alignment), alignment);

            return new Size(w, h);
        }

        boolean landscape = width >= height;
        int major = landscape ? width : height;
        int minor = landscape ? height : width;

        int minMajor = landscape ? caps.getMinWidth() : caps.getMinHeight();
        int maxMajor = landscape ? caps.getMaxWidth() : caps.getMaxHeight();

        if (maxMajor > major) {
            // Never increase the size
            maxMajor = major;
        }

        if (maxSize > 0 && maxMajor > maxSize) {
            maxMajor = maxSize;
        }

        int minMinor = landscape ? caps.getMinHeight() : caps.getMinWidth();
        minMinor = alignUp(minMinor, alignment);
        // Exclude the false prefix below the legal minor dimension before search.
        long minPixelsForMinor = ((long) minMinor * major + minor - 1) / minor;
        long minPixels = Math.max(minMajor, minPixelsForMinor);
        int minBlock = (int) Math.min(Integer.MAX_VALUE, (minPixels + alignment - 1) / alignment);
        int maxBlock = maxMajor / alignment;

        int bestBlock = BinarySearch.findHighestTrue(
                minBlock, maxBlock, block -> {
                    int pixels = block * alignment;
                    int w = align((int) ((long) width * pixels / major), alignment);
                    int h = align((int) ((long) height * pixels / major), alignment);
                    return withinCodecUpperBounds(caps, w, h);
                });

        int selectedPixels = bestBlock * alignment;
        boolean supported = bestBlock >= minBlock && caps.isSizeSupported(
                align((int) ((long) width * selectedPixels / major), alignment),
                align((int) ((long) height * selectedPixels / major), alignment));

        if (!supported) {
            Ln.d("No matching size found, ignore encoder size validation");
            // Do not query dependent SDK ranges with an unsupported fallback size.
            int fallbackMajor = Math.max(maxBlock * alignment, alignUp(minMajor, alignment));
            int fallbackMinor = Math.max(align((int) ((long) minor * fallbackMajor / major), alignment), minMinor);
            return landscape ? new Size(fallbackMajor, fallbackMinor) : new Size(fallbackMinor, fallbackMajor);
        }

        int bestMajor = bestBlock * alignment;
        int bestMinor;

        if (preserveAspectRatio) {
            bestMinor = align((int) ((long) minor * bestMajor / major), alignment);
        } else {
            // The minor dimension can potentially be extended
            int maxMinor = landscape ? caps.getMaxHeightForWidth(bestMajor) : caps.getMaxWidthForHeight(bestMajor);

            if (maxMinor > minor) {
                maxMinor = minor;
            }

            if (maxSize > 0 && maxMinor > maxSize) {
                maxMinor = maxSize;
            }
            bestMinor = align(maxMinor, alignment);

            // The major dimension can potentially be extended
            maxMajor = landscape ? caps.getMaxWidthForHeight(bestMinor) : caps.getMaxHeightForWidth(bestMinor);

            if (maxMajor > major) {
                maxMajor = major;
            }

            if (maxSize > 0 && maxMajor > maxSize) {
                maxMajor = maxSize;
            }
            bestMajor = align(maxMajor, alignment);
        }

        minMajor = alignUp(minMajor, alignment);

        if (bestMajor < minMajor) {
            bestMajor = minMajor;
        }

        if (bestMinor < minMinor) {
            bestMinor = minMinor;
        }

        int w = landscape ? bestMajor : bestMinor;
        int h = landscape ? bestMinor : bestMajor;

        assert w % alignment == 0 : "The width must be a multiple of alignment";
        assert h % alignment == 0 : "The height must be a multiple of alignment";

        return new Size(w, h);
    }

    /** A lower block-count limit must not turn the binary predicate false early. */
    private static boolean withinCodecUpperBounds(VideoSizeCapabilities caps, int width, int height) {

        if (width < caps.getMinWidth() || width > caps.getMaxWidth() || height < caps.getMinHeight() || height > caps.getMaxHeight()) {
            return false;
        }

        try {
            return height <= caps.getMaxHeightForWidth(width);
        } catch (IllegalArgumentException unsupportedWidth) {
            // Android also rejects widths whose coupled height range is empty.
            return false;
        }
    }

    public Size align(int alignment) {
        int w = align(width, alignment);
        int h = align(height, alignment);

        if (w == width && h == height) {
            return this;
        }
        return new Size(w, h);
    }

    private static int align(int value, int alignment) {
        return value / alignment * alignment;
    }

    private static int alignUp(int value, int alignment) {
        return (value + alignment - 1) / alignment * alignment;
    }

    public Rect toRect() {
        return new Rect(0, 0, width, height);
    }

    @Override
    public boolean equals(Object o) {

        if (this == o) {
            return true;
        }

        if (o == null || getClass() != o.getClass()) {
            return false;
        }
        Size size = (Size) o;
        return width == size.width && height == size.height;
    }

    @Override
    public int hashCode() {
        return Objects.hash(width, height);
    }

    @Override
    public String toString() {
        return width + "x" + height;
    }
}
