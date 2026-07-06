<h1 align="center">Local Intros Extended for Jellyfin</h1>
<h3 align="center">An enhanced fork of the official Jellyfin Intros Plugin</h3>

<p align="center">
<a href="https://github.com/dheinst/jellyfin-plugin-local-intros-extended/blob/main/LICENSE">
<img alt="GPL-3.0 License" src="https://img.shields.io/github/license/dheinst/jellyfin-plugin-local-intros-extended.svg"/>
</a>
</p>

## About

**Local Intros Extended** is a fork of the official Jellyfin Intros plugin that enables pre-roll intro videos from local storage. It replaces the simple flat rules list with a **powerful, unified Rule Engine** allowing you to combine multiple conditions for advanced playback control.

### Key Enhancements
*   **Sequential Evaluation (First-Match-Wins)**: Rules are evaluated from top to bottom. The first matching rule is applied. You can easily order rules using the ▲ and ▼ buttons in the UI.
*   **Frequency percentage chance**: Set a probability (0-100%) for each rule. If the rule matches, the specified chance determines whether it plays. If the roll fails, evaluation continues to the next rule.
*   **Playback Mode (Random vs Sequence)**: Play either one random intro from a rule's list or play all selected intros sequentially.
*   **Logical AND combinations**: Combine filters like *User*, *Library*, *Genre*, *Tag*, *Studio*, *Current Date range*, and *Release Date range* inside a single rule.
*   **Includes & Excludes Tags**: Renamed tags filter to "Includes Tags" and added a new "Excludes Tags" field to skip rules (e.g. for media tagged with `nointro`).
*   **Library (Collection Folder) Filter**: Restrict intros to specific libraries (e.g., play special intros only in your "Kids" or "Anime" libraries).
*   **Media Release Date Filter**: Check if the media premiere date falls within a configured range, allowing you to play vintage intros for retro movies.
*   **Rule Duplication**: Duplicate rules with a single click using the **Clone** button.
*   **Date Wraparound Bugfixes**: Fixed the date repeating check where weekly or monthly ranges crossing boundaries (e.g. Friday to Monday, or 28th to 3rd) would not trigger.
*   **Compact UI**: Clean grid layout for the rule builder with tooltips merged into the labels.

---

## Build

1. To build this plugin, you will need the [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (or newer).

2. Build the plugin with the following command:
   ```bash
   dotnet publish Jellyfin.Plugin.LocalIntrosExtended/Jellyfin.Plugin.LocalIntrosExtended.csproj --configuration Release --output bin
   ```

---

## Installation

1. Copy the compiled assembly `Jellyfin.Plugin.LocalIntrosExtended.dll` from the `bin/` folder.
2. In your Jellyfin data directory, navigate to the `plugins/` folder.
3. Create a new directory named `LocalIntrosExtended`.
4. Paste the `.dll` file into this directory:
   `plugins/LocalIntrosExtended/Jellyfin.Plugin.LocalIntrosExtended.dll`
5. Restart your Jellyfin server.

---

## Credits / Attribution

This project is a fork of the official [Jellyfin Intros Plugin](https://github.com/jellyfin/jellyfin-plugin-intros), which was originally created by [@dkanada](https://github.com/dkanada). Special thanks to them and all upstream contributors.

---

## License

This plugin is distributed under the GNU GPL-3.0 License. See the [LICENSE](./LICENSE) file for more information.
