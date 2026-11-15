# Gameplay status font

`GameplayStatus.asset` is a static 1024×1024 TextMeshPro atlas for the gameplay status messages and ASCII used by the desktop test HUD. Only the existing `Player_UI/state` and desktop session status use this asset.

Source: [Noto Sans CJK TC Regular](https://github.com/notofonts/noto-cjk/blob/main/Sans/OTF/TraditionalChinese/NotoSansCJKtc-Regular.otf), licensed under the SIL Open Font License 1.1; see `Editor/LICENSE.txt`.

The original font is retained under `Editor/` for reproducible atlas generation. The static TMP asset has no runtime source-font reference. To add new characters, update `ConfigureFruitGameplay.ConfigureFont` and run it through the Unity Editor.
