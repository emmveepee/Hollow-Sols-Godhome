# Native enemy cue library

Technical catalogue/evidence, not a live issue list or visual-acceptance certificate. Use [ROSTER-COMPLETION](ROSTER-COMPLETION.md) for current VFX corrections and player validation; [documentation authority](DOCUMENTATION.md) defines precedence.

The local gallery is `.local/effect-library/index.html`; open it in a browser. Search native enemy names (JieChuan = Jiequan), effect names or IDs, filter Visual / Sound / Native cue, copy an ID and request that effect for an attack. `catalog.json` records addresses and source relationships; `semantic-audit.json` records actual component ownership. Game-derived previews/metadata stay excluded from Git.

## Installed-build evidence

Nine Sols Steam21343987, Ver202512301623.bbde385f.0, Unity2022.3.62f3, Assembly-CSharp MVID dcdc095d-05f0-4390-897b-045fd51ebc28 / SHA25651f2921c61e237d7fc6a38f31de2c8bea8dd5826cf381d2fb8033b3c66afd743. Scan237 serialized metadata files:1,675,336 named objects,6,421 clips,43,392 sprites,50,562 particle systems,1,447,892 MonoBehaviours. Name scan and final semantic audit have zero errors. Engine resource metadata is resolved separately; no saves or game processes were accessed.

Current gallery:49 named visual candidates,37 native sprite sample loops/contact sheets,34 authored cue-related sound events with108 distinct decoded media samples, and five native cue graphs. All6,952 named warning objects have exact hierarchy/component records. Complete MonoBehaviour census resolves7,036 warning/weakness/sound contexts, including2,827 FxPlayer emitters,2,512 SoundPlayers,1,306 weakness managers and391 native kick activators.

The native warning family consists of standard, boss/big and small pools. All1,306 inspected weakness managers share the same outline/afterimage prefab pair; their per-enemy SpriteFlasher identities remain independent. The fifth pooled graph is weakness-break feedback. This does not mean every enemy-specific release/swing/projectile artwork is the same.

## Preview contracts and remaining coverage

- GIFs are isolated native sprite samples, sorted by native sample suffix and displayed at10samples/s for browsing. They are **not** reconstructed Unity animation timing. Contact sheets retain every selected sample. Body-only references are listed without misrepresenting them as isolated VFX.
- Raw Wwise samples preserve actual event→media→bank lineage. They omit live bus gain, random selection, event layering and spatialization. The gallery has audio controls, never autoplay. Full3,697-distinct-event metadata is indexed separately for unnamed cue follow-up.
- Semantic audit uses exact script identities and reviewed public serialized field prefixes, guarded by the installed assembly hash. It discovers warning emitters with arbitrary GameObject names; it is not restricted to the convenient Horseman donor.
- Source art, native-rendered preview and canvas admission are separate evidence tiers. The new catalog does not automatically authorize every graph in ordinary combat. Optimized controllers, particle simulation and material-driven outlines need the planned native preview room. Unnamed enemy-specific artwork outside the named clip search is not yet exhaustively classified.
- No new gameplay tuning, damage mapping, save access, donor activation, deployment or game launch occurred for this offline milestone. The user’s deployed playtest build remains28f4d93.

## Reproduction

Own tooling is under `tools/effects/`. Use the bundled Python or another compatible Python with the reviewed local UnityPy1.25.4/Pillow dependencies. Installed game data is `C:/Steam/steamapps/common/Nine Sols/NineSols_Data`. Run from repository root:

```powershell
$python = 'python' # Or the path to your deliberately provisioned Python interpreter.
& $python tools/effects/native_library.py scan --game-data 'C:/Steam/steamapps/common/Nine Sols/NineSols_Data' --output .local/effect-library --dependencies .local/tools/unitypy
& $python tools/effects/native_library.py catalog --game-data 'C:/Steam/steamapps/common/Nine Sols/NineSols_Data' --output .local/effect-library --dependencies .local/tools/unitypy --decoder .local/deps/vgmstream/r2117/vgmstream-cli.exe
& $python tools/effects/native_library.py semantic --game-data 'C:/Steam/steamapps/common/Nine Sols/NineSols_Data' --output .local/effect-library --dependencies .local/tools/unitypy
```

Catalog/semantic phases reject changed assembly/bundle fingerprints. Sparse resource reads retain at most32MiB decompressed bundle blocks; extraction writes only inside repository `.local`. Never place the gallery/assets in plugin build output or tracked documentation. Scanner reruns may refresh local derived files; they never write installed assets.

Portable decoder: official vgmstream r2117 / commit71e2361042531fe767fb98300cf8c1ee95e539a0, win64ZIP SHA2566c4a8a3813864fefed081bbd337dbc0ad93bf88e0b92f5db98d7ab258b22dc6c (matches official release digest). Pinned COPYING/USAGE are retained in `.local/deps/vgmstream`; permissive notice reviewed before CLI execution. No implementation or decoder binary is copied into Git. WEM decoding uses `-i -o` with a30second per-sample deadline and WAV duration/channel/rate validation.

## Native viewer progress and current deployment

Experimental F9 viewer source has five initial native presentations (standard/large warning, outline, afterimages, existing Jiequan released-art adapter), one effect at a time with replay/sound/return controls. It is not yet deployed for player use: exact28f4d93 was restored when the user began playtesting. Native test0fd8dd28 verifies changing host pixels and room teardown, but actual captures exposed missing afterimage roots; latest ownership correction is compiled and awaiting runtime verification. The current room does not contain all49 enemy-specific complete attack animations. Small warning donor73 and weakness-break admission remain pending. Source-only GIFs retain their explicit timing/shader limits.

Artifact integrity: bundled Python tools/effects/verify.py --dependencies .local/tools/unitypy validates88 unique IDs and182 local files (184 references), producing ignored verification.json. This verifies file integrity, not native animation completeness. Exact six-component rollback: close both games, tools/Restore-FoundationCheckpoint.ps1 -Checkpoint28f4d93 -CheckOnly; omit CheckOnly to restore. Use a space between -Checkpoint and28f4d93 when executing.
