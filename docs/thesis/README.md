<!-- Copyright (c) Files Community. SPDX-License-Identifier: MPL-2.0 -->

# ReFiles thesis

The thesis source is in `thesis.tex`. It uses a two-sided A4 book layout with mirrored margins. The title and abstract are isolated on right-hand pages, and the Introduction begins on a right-hand page with Arabic page numbering.

Build from the repository root with:

```powershell
pdflatex -interaction=nonstopmode -halt-on-error -output-directory=output/pdf docs/thesis/thesis.tex
pdflatex -interaction=nonstopmode -halt-on-error -output-directory=output/pdf docs/thesis/thesis.tex
```

The generated document is `output/pdf/thesis.pdf`.
