# ![Meppy icon](icon32x.png) Meppy

Meppy is a lightweight software used to resize and move windows like they used to be when they got closed or when the PC restarted. It runs in the background and automatically detects new windows from the processes whitelist.

## Presentation

Every time a process opens a Main Window (very important, it won't work on modals or child windows, there is no way to target certain windows), it relocates it to the saved data from where it was last closed. It also works with apps that display a splash screen (like Discord), but it may be bugged some times.

The software is configurable through an interface, available in the system tray :

![image](https://user-images.githubusercontent.com/30344403/205997834-8ca77251-a377-40aa-82fc-194ca38b7e87.png)

![image](https://user-images.githubusercontent.com/30344403/205995698-bb385740-0635-4b8b-a496-e706c001bec8.png)

You can quickly set which active window to target, or add a new process by specifying an exe file.

You can also click on the ![eye icon](Meppy/eye.png) button to display a red rectangle on the active window, or the last known position :

![image](https://user-images.githubusercontent.com/30344403/205996839-b7a4cd4e-4e84-4934-9d10-8074d4c48e86.png)

## Download

Either download the release from [here](https://github.com/Wiltoga/Meppy/releases/latest), or download the sources and build the app.
