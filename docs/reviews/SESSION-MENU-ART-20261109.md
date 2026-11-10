# Private session menu and startup artwork

The desktop picker, private EFT startup and profile-loading view show the selected *Streiflicht* training course. Sun from the left windows crosses the plywood lanes. At home, the concrete barrier and nearby foreground shift against the far targets, by up to five and twelve pixels per axis at 1080p. The parallax settles at rest and the native controls stay in place.

## Where it appears

- The client installs its art hooks after Core admission and a positive private-session gate report. An ordinary EFT launch follows the native UI path.
- On startup, a `SplashScreenPanel.Awake` postfix and a one-time search for an already active panel cover the two possible initialization orders. EFT positions a 16:9 image and four stretched margins inside its full-width image group. Arena adds one full-width surface to that group and uses a centered cover crop. During the native fade it hides the five stock renderers through their individual alpha values. Closing the panel releases the art and restores the saved renderer alpha where Arena still owns it. EFT owns the footer, input and completion.
- EFT loads the menu environment before it closes the boot panel. A passive canvas on that environment displays the flat course above its scenery at sorting order −1. Login, profile status, home controls and preloader windows draw above it. The boot panel and loading canvas each hold a reference to the same texture, so closing either keeps it available to the other.
- Native screen-change events identify profile loading, the home controller and other destinations. The loading canvas stays behind the home fade until that controller's parallax layers are visible and its canvas is opaque. Other destinations and raid start release it. The return-to-menu operation covers the interval while EFT rebuilds its menu screens.
- In EFT the home, pause and reconnect menus share a `MenuScreen`. Only `MainMenuScreenController` receives parallax. Its first child holds three mouse-transparent images behind the controls. A soft gradient keeps the lower menu text legible. The native canvas sets the viewport and fade. Leaving the menu releases its layers.
- In the direct-start picker the flat image sits above the existing controls. Its lifetime ends with the form.

## Beta notice and raid version line

The private home controller masks its native beta notice before the backdrop loads. The mask covers the `_alphaWarningGameObject` panel and its text. The reconnect warning belongs to a separate object. EFT retains ownership of each panel's active state and button layout.

A confirmed Arena raid masks the preloader's `_alphaVersionLabel`. EFT and SPT can still update the underlying text while the raid runs. The mask follows the exact game/world pair through respawn and is released at raid end, on a change of world, or at plugin shutdown. Ordinary raids use their native corner label. The neighboring notifier, taskbar, game-mode controls and client watermark have their own owners.

The reference mod identified the two native controls. ArenaMode scopes their visibility to the home controller and raid using its own canvas groups. [Beta panel reference](https://github.com/redlaser42/SPT-HideBetaRework/blob/eec4b624d3b0808d5159c9ea3497a6608d0bc6e7/HideBetaPatch.cs) · [Version label reference](https://github.com/redlaser42/SPT-HideBetaRework/blob/eec4b624d3b0808d5159c9ea3497a6608d0bc6e7/HideVersionPatch.cs)

## Assets and references

The client embeds all four images. The helper embeds the same flat JPG that the user selected. All layers use a common 1680 × 720 canvas and a centered cover crop. Overscan covers the largest cursor offset. The package check also composites the three layers at rest and compares their colors with the selected flat artwork. [Artwork provenance](../../ArenaMode.Client/Assets/SessionMenu/SOURCE.md)

The linked mods provided technical references for catching an already active startup panel and distinguishing the main menu controller. ArenaMode supplies its own artwork and code. [CLASSIFIED](https://github.com/JoelHauser/CLASSIFIED) · [WTT Menu Overhaul](https://github.com/emilanderss0n/SPT-Menu-Overhaul/)
