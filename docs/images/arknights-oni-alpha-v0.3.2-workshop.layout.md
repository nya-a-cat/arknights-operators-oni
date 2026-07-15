# Alpha v0.3.2 workshop image layout

The workshop image is a deterministic montage of four unmodified Steam screenshots. The renderer only crops and scales screenshot pixels, draws borders and colour blocks, and adds text. It creates no new character, environment, animation, or game-effect pixels.

## Sources and labels

- `20260715125124_1.jpg` — `EXUSIAI / 能天使`
- `20260715140728_1.jpg` — `SURTR / 史尔特尔`
- `20260715140920_1.jpg` — `AMIYA / 阿米娅`
- `20260715141342_1.jpg` — `TEXAS / 德克萨斯`

Each 1920×1080 source uses crop rectangle `x=660, y=250, width=480, height=800`, scaled into a 430×750 card. The exact title, subtitle, colours, dimensions and output path are recorded in [`tools/render_alpha_promo.ps1`](../../tools/render_alpha_promo.ps1).
