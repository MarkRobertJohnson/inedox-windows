Stop-Service -Name INEDOOTTERWEBSVC
Stop-Service -Name INEDOOTTERSVC

Copy "C:\dev\ws\inedox-windows\Windows\InedoExtension\bin\Debug\Windows.*" "C:\ProgramData\Otter\ExtensionsTemp\Web\Windows" -Force

Start-Service -Name INEDOOTTERSVC
Start-Service -Name INEDOOTTERWEBSVC
