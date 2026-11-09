# Private session menu and startup artwork

The session helper's desktop profile picker and the private EFT startup panel show the same Factory hall. The home menu separates it into a still hall, a concrete barrier and a foreground gate. At 1080p the barrier follows mouse movement by up to five pixels per axis and the gate by twelve, in the opposite direction to the pointer. The composition settles at rest. All controls retain their native positions.

## Where it appears

- The client installs both hooks after Core admission and a positive private-session gate report. An ordinary EFT launch follows the native UI path.
- On startup, a `SplashScreenPanel.Awake` postfix and a one-time search for an already active panel cover the two possible initialization orders. EFT positions a 16:9 image and four stretched margins inside its full-width image group. Arena adds one full-width surface to that group and uses a centered cover crop. During the native fade it hides the five stock renderers through their individual alpha values. Closing the panel releases the art and restores the saved renderer alpha where Arena still owns it. EFT owns the footer, input and completion.
- In EFT the home, pause and reconnect menus share a `MenuScreen`. Only `MainMenuScreenController` receives the background. Its first child holds three mouse-transparent images at the back of the screen. A soft gradient behind the lower menu keeps its text legible against the lit doorway. The native canvas sets the viewport and fade. Leaving the menu releases the images. A return to the home menu rebuilds the layers.
- In the direct-start picker the flat image sits above the existing controls. Its lifetime ends with the form.

## Assets and references

The client embeds all four images. The helper embeds the same flat image. All layers share a 2352 × 1008 canvas and a centered cover crop. Their overscan covers the largest cursor offset. The exporter and the three original masters are checked in. [Artwork provenance](../../ArenaMode.Client/Assets/SessionMenu/SOURCE.md)

The linked mods provided technical references for catching an already active startup panel and distinguishing the main menu controller. ArenaMode supplies its own artwork and code. [CLASSIFIED](https://github.com/JoelHauser/CLASSIFIED) · [WTT Menu Overhaul](https://github.com/emilanderss0n/SPT-Menu-Overhaul/)
