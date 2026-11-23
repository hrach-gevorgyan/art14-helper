# Third-party notices

Art14 Helper itself is licensed under the GNU General Public License v3 (see [LICENSE](LICENSE)).
It is compiled into one native executable. These parts of it come from others, under
licenses compatible with the GPL:

| Component | Use in the app | License |
|---|---|---|
| .NET runtime and libraries (compiled in by NativeAOT) | Runtime, collections, HTTP, file and registry access | MIT, © .NET Foundation and Contributors |
| System.Drawing.Common | GDI+ drawing: tray icon, popup, settings window shapes | MIT, © .NET Foundation and Contributors |

Only for building and testing (not shipped in the exe):

| Component | License |
|---|---|
| xUnit v3 (xunit.v3, xunit.runner.visualstudio) | Apache 2.0 |
| Microsoft.NET.Test.Sdk | MIT |

The MIT license text: <https://opensource.org/license/mit>. The Apache 2.0 license text:
<https://www.apache.org/licenses/LICENSE-2.0>.

The app ships no files from HONOR. It reads, on the user's own laptop, the factory color
measurement HONOR installed there, and calls the Intel graphics driver's ControlLib.dll
that comes with the driver.
