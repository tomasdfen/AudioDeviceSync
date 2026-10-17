# AudioDeviceSync

Pequeño demonio para Windows que mantiene **siempre sincronizados** el dispositivo de audio *predeterminado* y el de *comunicaciones*, tanto para salida (reproducción) como para entrada (grabación).

Si cambias uno de los dos en Ajustes de sonido (o cualquier aplicación lo cambia), el otro pasa automáticamente al mismo dispositivo.

## Comportamiento

Windows tiene tres roles por cada dirección de audio: **Consola**, **Multimedia** y **Comunicaciones**. "Predeterminado" en la interfaz de Windows equivale a Consola + Multimedia. El demonio los mantiene los tres apuntando al mismo dispositivo, y lo hace por separado para reproducción y para grabación.

- **Quién gana:** el rol que cambió más recientemente. Si cambias solo Comunicaciones, Consola y Multimedia lo siguen; si cambias el predeterminado, Comunicaciones lo sigue.
- **Conflictos:** si varios roles cambian a la vez a dispositivos distintos (por ejemplo, al desenchufar un dispositivo y que Windows elija sustitutos distintos), prevalece el orden Consola > Multimedia > Comunicaciones.
- **Al arrancar:** si ya estaban desincronizados, gana el predeterminado (Consola).
- **Detección:** reacciona a las notificaciones de Windows sobre cambios de dispositivo (espera 250 ms a que Windows termine de aplicar varios cambios) y, como red de seguridad, revisa el estado cada 5 s por si el servicio de audio se reinicia y se pierden las notificaciones.
- **Sin bucles:** solo actúa cuando los roles difieren, así que sus propios cambios no lo vuelven a disparar.
- **Una sola instancia:** si se lanza una segunda, termina en silencio. No muestra ventana.
- **Errores:** ante una excepción se reconecta a la API de audio en la siguiente iteración.

## Instalación

No requiere administrador ni instalar nada: usa el compilador de .NET Framework que ya incluye Windows.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

El script:

1. Compila `AudioDeviceSync.cs` a `%LOCALAPPDATA%\AudioDeviceSync\AudioDeviceSync.exe`.
2. Registra la tarea programada `AudioDeviceSync`, que se ejecuta **al iniciar sesión** del usuario (los dispositivos predeterminados son por usuario, por eso no es un servicio de sistema). Sin límite de tiempo, con reinicio automático (hasta 5 veces, cada minuto) si falla, y también con batería.
3. Arranca la tarea inmediatamente.

Volver a ejecutarlo actualiza una instalación existente.

## Desinstalación

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1
```

Elimina la tarea, detiene el proceso y borra `%LOCALAPPDATA%\AudioDeviceSync`.

## Log

`%LOCALAPPDATA%\AudioDeviceSync\log.txt` registra el arranque, cada cambio realizado (flujo, rol, dispositivo anterior y nuevo, código HRESULT) y los errores. Se borra al superar 256 KB.

## Notas técnicas

- Los cambios se aplican con la interfaz COM no documentada `IPolicyConfig`, la misma que usan otras utilidades de audio. Funciona desde Windows 7, pero Microsoft no garantiza su estabilidad.
- Los dispositivos se identifican por su ID de endpoint de Windows (no por nombre).

## Archivos

| Archivo | Función |
|---|---|
| `AudioDeviceSync.cs` | Código del demonio |
| `install.ps1` | Compila, instala y registra el inicio automático |
| `uninstall.ps1` | Desinstala |
