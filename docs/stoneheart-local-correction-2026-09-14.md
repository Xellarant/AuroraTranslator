# Stoneheart local Erupting Earth correction — September 14, 2026

The user explicitly authorized correcting Stoneheart's unresolved spell grant to
Xanathar's Guide to Everything. This is a recorded local assumption about the
homebrew author's intended first-party reference, pending confirmation with
KibblesTasty. It is not evidence of an upstream correction or a general alias rule.

Installed file:
`C:\Users\Ralla\Documents\5e Character Builder\custom\user\local\sorcerer-stoneheart.xml`.

The original remains unchanged at
`reddit/reddit-unearthed-arcana/kibblestasty/sorcerer-stoneheart.xml` under the same
content root. Its Source element identifies **Stoneheart Sorcerer**, by
**KibblesTasty**, marks it as homebrew, and links to
`https://www.gmbinder.com/share/-MCVeS57lDErNlwOKp1F`.

The sole gameplay edit is within `ID_KT_SS_ARCHETYPE_SORCERER_STONEHEART`:

```xml
<!-- Before -->
<grant type="Spell" id="ID_PHB_SPELL_ERUPTING_EARTH" spellcasting="Sorcerer" known="true" level="5" />
<!-- After -->
<grant type="Spell" id="ID_XGTE_SPELL_ERUPTING_EARTH" spellcasting="Sorcerer" known="true" level="5" />
```

Both real spell definitions remain independent:
`ID_POTA_SPELL_ERUPTINGEARTH` and `ID_XGTE_SPELL_ERUPTING_EARTH`. The correction
adds no second grant and changes no other grant attributes or gameplay content.

The full local file carries one v1 `replace` operation, with an embedded complete
original baseline, its declaration fingerprint, the user-authorized rationale,
and `review-pending` state. Existing `info/update` metadata is preserved. The
reason explicitly records pending confirmation with KibblesTasty; no publisher
contact was attempted. Unchanged companion declarations can continue to follow
authoritative updates through the existing correction evaluator.

Validation before installation:

- The updated protected-repair regression passed, including all nine reference
  corrections, both real Erupting Earth IDs, and Stoneheart's mirrored rationale
  and `review-pending` state.
- The actual full-file correction passed an isolated `sqlite-import` into
  `artifacts/stoneheart-validation/candidate.sqlite`. Its grant resolved to the
  XGTE ID, both definitions remained, integrity was `ok`, and foreign-key checks
  reported zero errors.
- Parsing and comparing the original, embedded baseline and corrected document
  confirmed the grant ID was the only gameplay change.
- Installation refused changed input hashes or an existing different local file.
  The installed file matched the tested artifact hash.

Evidence: `artifacts/stoneheart-validation/evidence.json`.

| File | SHA-256 |
| --- | --- |
| Original source | `996291A1F67DAA706860D8E8AE775FCC6787FCBAC75FC761B5350CB704F6CBDC` |
| Installed local correction | `C698C7F6DA7448FF0146A0D58B8DE29A222CA51E382F2B38C36ACE65611518D4` |

Only Stoneheart's local correction was installed. The other eight reference
repairs remain repository artifacts. No production database refresh, executable
publication, commit or push was performed. The focused test and temporary import
were sufficient for this data correction; the full harness was not rerun.
