<!-- Copyright (c) Files Community. SPDX-License-Identifier: MPL-2.0 -->

# ReFiles thesis

The thesis source is in `thesis.tex`. It uses a two-sided A4 article layout. The first page places the title, author details, centered abstract, and Introduction in the compact style of an arXiv paper. The paper covers the system architecture, capability model, progressive browsing, ownership, provider case studies, evaluation, related systems, limitations, and future work.

Build from the repository root with:

```powershell
pdflatex -interaction=nonstopmode -halt-on-error -output-directory=output/pdf docs/thesis/thesis.tex
pdflatex -interaction=nonstopmode -halt-on-error -output-directory=output/pdf docs/thesis/thesis.tex
```

The generated document is `output/pdf/thesis.pdf`.
