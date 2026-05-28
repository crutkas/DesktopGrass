// InternalsVisibleTo.cs
//
// Expose the Sim's internal API surface (Prng, Blade, InputEvent, Stroke,
// Constants, plus the Test* helpers on Sim) to the unit-test project. The
// runtime app code keeps these types and methods internal — the test
// project is the only consumer that needs to peek under the lid.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DesktopGrass.WinUI3.Tests")]
