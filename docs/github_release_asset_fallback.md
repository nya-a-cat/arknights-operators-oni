# GitHub Release 全干员备用资源方案

## 业务结论

项目选择 GitHub Release 作为 PRTS 的备用中转，Git 源码仓库继续只保存代码、轻量目录、生成工具和文档。备用快照覆盖当前目录内 449 个干员及其全部皮肤/模型，每个干员对应一个独立 ZIP。Mod 只有在 PRTS 元数据解析或资源下载失败时才访问备用源。

当前代码、测试和 GitHub Actions 工作流已准备完成。`assets-v1.0.0` 仍未上传或发布；需要维护者明确确认后推送代码、手动触发工作流，并在核对结果后发布草稿 Release。

## 为什么使用 Release

- Git 历史不会被大量二进制反复放大，普通 clone 和源码审查保持轻量。
- 一个快照只使用 449 个干员包和 1 个清单，低于 GitHub 每个 Release 1000 个 asset 的限制。
- 每个 Mod 版本固定引用明确的资源 tag；发布后可启用 immutable releases，锁定 tag 和 asset，并获得 release attestation。
- 单次故障回退只下载当前干员包。用户电脑不会预下载全量 449 干员资源。

GitHub 当前文档说明，单个 Release asset 必须小于 2 GiB，单个 Release 最多包含 1000 个 asset；GitHub 同时保留对显著过量带宽进行限制的权利。因此本方案把 Release 定位为故障备用源，并保留按需缓存：[About releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)、[Immutable releases](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases)、[Acceptable Use Policies](https://docs.github.com/en/site-policy/acceptable-use-policies/github-acceptable-use-policies)。

## 两类故障与处理

| 故障 | 处理 |
| --- | --- |
| PRTS `meta.json` 路径、字段或层级变化 | Mod 从固定 Release tag 下载稳定清单，按清单内已解析的皮肤、模型和文件路径继续访问 PRTS 文件域 |
| PRTS 文件域超时、404、内容损坏或 hash 不符 | Mod 下载当前干员 ZIP，校验包 hash，逐项校验长度与 hash，再把当前外观写入现有缓存 |
| GitHub 暂时不可用 | 已缓存的清单、资源与干员包继续可用；冷缓存同时失去 PRTS 和 GitHub 时保留原复制人外观并记录聚合错误 |

## 运行时流程

1. 使用当前 PRTS `meta.json` 解析并下载选中外观。
2. 主路径失败后，从 `assets-v1.0.0` 获取 `operator-asset-fallback-manifest-v1.json`；该清单按固定版本缓存。
3. 根据 `character_id + skin + model` 选择清单项，先尝试清单中固定的 PRTS 文件 URL 和 SHA-256。
4. 固定文件路径也失败后，下载 `operator-{character_id}.zip`。
5. 校验 ZIP 的长度和 SHA-256，只读取清单列出的 archive entry，限制解压长度并逐项计算 SHA-256。
6. 当前外观的 atlas、skel 和纹理全部校验后写入共享缓存，并沿用 512 MiB LRU 或永久保留策略。

运行时只接受以下网络边界：

- `https://torappu.prts.wiki/`
- `https://static.prts.wiki/`
- `https://github.com/nya-a-cat/arknights-oni/releases/download/`
- GitHub Release 下载产生的 `release-assets.githubusercontent.com` / `objects.githubusercontent.com` HTTPS 重定向

## 清单合同

根对象包含：

- `schema_version`：当前为 `1`。
- `snapshot_id`：人工可读的 PRTS 快照标识。
- `release_tag`：资源快照 tag。
- `operators`：全部干员包记录。

每个干员包包含：

- `character_id`、`character_name`。
- `package_url`、`package_length`、`package_sha256`。
- `appearances`：皮肤、模型、资源版本和文件列表。

每个文件包含：

- `role`：`atlas`、`skel` 或 `page`。
- `relative_path`：现有共享缓存中的目标路径。
- `archive_path`：ZIP 内精确 entry 名。
- `source_url`：生成快照时确认的 PRTS 文件 URL。
- `length`、`sha256`。

Mod 拒绝未知 schema、不完整条目、路径逃逸、非许可域、长度不符、hash 不符、缺失 atlas page 和异常解压膨胀。

## GitHub Actions 生成

工作流 `.github/workflows/build-operator-fallback-release.yml` 只能手动触发：

1. 创建或复用指定 tag 的草稿 Release。
2. 把 449 干员分成 8 个 shard，每个 runner 串行处理自己的干员，控制对 PRTS 的并发压力。
3. 每个 runner 获取元数据和全部皮肤/模型资源，生成确定性 ZIP、逐文件记录和 partial manifest。
4. 8 个 shard 全部成功后合并清单，并强制检查干员数量为 449。
5. 上传最终清单，Release 继续保持 draft。

100 MB 是本机磁盘空间偏好。GitHub runner 对达到 100 MiB 的单干员包输出警告并继续；运行时对 ZIP 保留 512 MiB 的异常响应/压缩炸弹技术上限。当前单个 Spine 源文件仍使用既有 64 MiB 安全上限。若真实干员包触发 512 MiB 上限，需要按该干员的外观拆包并升级 manifest schema，不能直接放宽到无限下载。

## 发布验收

发布 `assets-v1.0.0` 前必须满足：

- 8 个 shard 全部成功，合并清单正好包含 449 个唯一 `character_id`。
- Release asset 数量为 450：449 个干员 ZIP 和 1 个最终清单。
- 工作流没有 512 MiB 阻断；达到 100 MiB 的报告逐项人工确认。
- 随机抽取至少 3 个干员，并覆盖默认/皮肤、多页 atlas；下载包后重新计算 package 与文件 SHA-256。
- 本地 `run_operator_fallback_tests.sh`、完整构建和现有资源集成测试通过。
- 发布草稿前确认仓库已经启用 immutable releases；发布后用 `gh release verify` / `gh release verify-asset` 验证完整性。

## 回滚

- 代码回滚：撤销引入备用清单、包安装器和工作流的聚焦 commit；现有 PRTS 主路径恢复为唯一网络来源。
- 资源回滚：Mod 固定引用 tag，不会自动跳到新快照。发现问题时发布新的资源 tag 和修复版 Mod；已发布的 immutable tag 不覆盖、不复用。
- 未发布草稿：删除草稿和未使用 tag 即可，不影响当前 `v0.3.2-alpha.1` 用户。
