using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ScrcpySeamless.Infrastructure.Tests")]
// Production-backed Desktop lifecycle tests use the existing controlled process seam.
[assembly: InternalsVisibleTo("ScrcpySeamless.Desktop.Tests")]
