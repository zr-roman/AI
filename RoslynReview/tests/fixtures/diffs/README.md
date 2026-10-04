# Fixture diffs

Real `git diff` output against hand-made earlier versions of the SampleShop files.
The new side of every diff is the current content of `../SampleShop`, which
`FixtureDiffTests` checks line by line.

| File | What it exercises |
|------|-------------------|
| `01-feature.diff` | Added and modified members, a doc-comment edit, a new using directive, a new file with a record, a deleted file, a non-C# file, a file outside the solution, top-level statements |
| `02-removals.diff` | Removals only: a statement inside a method, an attribute, a whole method, a blank line |
| `02-removals-u0.diff` | The same change with zero lines of context (`git diff -U0`) |

If you edit a SampleShop file that a diff touches, regenerate that diff or add a new scenario instead.
