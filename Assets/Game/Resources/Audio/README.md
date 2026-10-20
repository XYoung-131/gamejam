# 游戏音频

| 资源 | 用途 |
| --- | --- |
| `Music/MainMenu.mp3` | 主页背景音乐（Project_2） |
| `Music/Chapter1.mp3` | 第一章背景音乐（Project_14） |
| `Music/Chapter2.mp3` | 第二章背景音乐（Project_3） |
| `SFX/EnemyAttack.wav` | 小怪攻击 |
| `SFX/EnemyHit_Blood.wav` | 小怪受击变化 1 |
| `SFX/EnemyHit_Flesh.wav` | 小怪受击变化 2 |

播放逻辑集中在 `Assets/Game/Scripts/Core/GameAudioManager.cs`。背景音乐随场景和
`RunProgression.ChapterIndex` 自动切换，音频管理器跨场景常驻。
