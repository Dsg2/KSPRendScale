# KSPRendScale
A ksp mod to change the render scale. Compatible with all graphics mods.
This can reduce GPU load or reduce Vram usage.

Known issues:
 - buttons displayed in world space are broken (but doesn't have any impact on gameplay, since KSP provides arrow key navigation or UI buttons for all cases).
 - world space target vessel indicators are TUFXoffset.
 - some scale percentages may cause artifacts
 - motion blur setting in TUFX may case screen to become permanently blurry

# Showcase (Intel Iris Xe):
## Menu screen: (measuring GPU load)
100% render scale: 100% GPU usage
<img width="1277" height="715" alt="KSP-RS-menu100load100" src="https://github.com/user-attachments/assets/9acd9090-c3e7-45d2-8822-d953ce561add" />
80% render scale: 64% GPU usage
<img width="1275" height="714" alt="KSP-RS-menu80load64" src="https://github.com/user-attachments/assets/557b27b9-d21c-4913-b3b7-fe8ae591c3b4" />
60% render scale: 45% GPU usage
<img width="1276" height="712" alt="KSP-RS-menu60load45" src="https://github.com/user-attachments/assets/902aac4e-82ca-43e6-8fcf-ff56bcc1b885" />
## KSC: (measuring FPS)
100% render scale: 38fps
<img width="1276" height="715" alt="KSP-RS-ksc100load100fps38" src="https://github.com/user-attachments/assets/331e3fc7-44cc-4fe1-b822-86e85eb3d7de" />
80% render scale: 44fps
<img width="1276" height="719" alt="KSP-RS-ksc80load100fps44" src="https://github.com/user-attachments/assets/2f2314b3-6ea0-4812-b2c3-ca8714cef1e9" />
60% render scale: 51fps
<img width="1279" height="717" alt="KSP-RS-ksc60load100fps51" src="https://github.com/user-attachments/assets/18ee2de4-cdb0-49d4-99ed-76e7a5a91bdc" />
## Flight (measuring FPS, weather conditions changed so a fair test couldn't be recreated)
100% render scale: 26fps
<img width="1277" height="719" alt="KSP-RS-flight100load100fps26" src="https://github.com/user-attachments/assets/b460389c-60d6-4916-938a-80f7c1302126" />
100% render scale: 31fps
<img width="1274" height="713" alt="KSP-RS-flight80load100fps31" src="https://github.com/user-attachments/assets/4c73855b-7142-4cef-8ea0-7e29fef7b600" />
