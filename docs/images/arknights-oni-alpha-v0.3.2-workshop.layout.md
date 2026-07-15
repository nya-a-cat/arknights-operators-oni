# Alpha v0.3.2 workshop image layout

The workshop image is a deterministic montage of four unmodified Steam screenshots. The renderer only crops and scales screenshot pixels, draws borders and colour blocks, and adds text. It creates no new character, environment, animation, or game-effect pixels. The header explicitly calls out the automatically localized UI and Chinese, English, and Japanese operator search.

GPT ImageGen was not used for this image, so there is no generation prompt. The complete inputs are stored under [`source/alpha-v0.3.2`](./source/alpha-v0.3.2), and the renderer is [`tools/render_alpha_promo.ps1`](../../tools/render_alpha_promo.ps1).

## Sources and labels

| Source | Label | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| `20260715125124_1.jpg` | `EXUSIAI / 能天使` | 488,521 | `43E9B53AA7ED6E1DAF475D9BF8115048B5DB968066EDF069FF8F6CDACFBF5A75` |
| `20260715140728_1.jpg` | `SURTR / 史尔特尔` | 514,832 | `5352DC7177AD7BD5E38E84A77A883CBAE90BB90ED3371A37B3F9656DD16AB49E` |
| `20260715140920_1.jpg` | `AMIYA / 阿米娅` | 513,038 | `4A34B72AE980A0241AB5886A8F981AFC2C79B37375B5810464CDA8780EA1DAC5` |
| `20260715141342_1.jpg` | `TEXAS / 德克萨斯` | 501,873 | `DBC6E76E10B8FE7F28525D6E6EEFCFCD43EAEB04A2B45BB0D5872C92CE2E3E25` |

Each 1920×1080 source uses crop rectangle `x=790, y=640, width=240, height=380`, scaled into a 430×680 image area. The separate 430×70 label bar begins below the image at `y=925`, so it does not cover the character's feet. The exact title, subtitle, colours, dimensions and output path are recorded in [`tools/render_alpha_promo.ps1`](../../tools/render_alpha_promo.ps1). Rendering requires the Windows `Microsoft YaHei UI` font.

## Reproduce

Run from the repository root on Windows PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\render_alpha_promo.ps1
```

The default command reads the archived source screenshots and replaces `docs/images/arknights-oni-alpha-v0.3.2-workshop.png`. Use `-ScreenshotRoot` only when intentionally testing a different four-file capture set.

Current output: 1,699,388 bytes; SHA-256 `A36C1A7525F14A301171EA6659F19D884C5924B5A52877B069E9E38DE964625A`.
