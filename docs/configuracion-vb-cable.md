# Configurar VB-CABLE con SoundDeck

## Instalación

1. Descarga VB-CABLE desde su [sitio oficial](https://vb-audio.com/Cable/).
2. Extrae el ZIP, ejecuta `VBCABLE_Setup_x64.exe` como administrador y pulsa
   **Install Driver**.
3. Reinicia Windows. SoundDeck no instala ni redistribuye el controlador.

## SoundDeck

1. En **Micrófono**, selecciona el dispositivo con el que hablas.
2. En **Salida local**, selecciona tus auriculares o altavoces.
3. En **Salida virtual**, selecciona `CABLE Input (VB-Audio Virtual Cable)`.
4. Deja cada sonido en **Ambas** para escucharlo localmente y enviarlo a la
   conversación, o elige una sola ruta.

SoundDeck captura el micrófono y lo mezcla con el sonido hacia `CABLE Input`.
No actives “Escuchar este dispositivo” en Windows: suele crear eco.

## Discord

1. Abre **Ajustes de usuario > Voz y vídeo**.
2. Selecciona `CABLE Output (VB-Audio Virtual Cable)` como entrada.
3. Desactiva el control automático de ganancia si corta sonidos breves.
4. Ajusta la sensibilidad de entrada y realiza la prueba de micrófono.

En un juego, el paso es equivalente: usa `CABLE Output` como micrófono.

## Solución de problemas

- **No aparece VB-CABLE:** reinicia Windows y comprueba en
  **Configuración > Sistema > Sonido > Todos los dispositivos** que CABLE Input
  y CABLE Output estén habilitados.
- **No se oye tu voz:** comprueba el micrófono elegido y que no esté silenciado
  en SoundDeck.
- **Hay distorsión:** reduce el volumen del micrófono o la ganancia del sonido.
- **Hay eco:** usa auriculares y desactiva la monitorización de CABLE Output.
- **Discord corta el efecto:** desactiva temporalmente supresión de ruido,
  cancelación de eco y puerta de ruido para aislar la causa.
- **Un dispositivo se desconecta:** vuelve a seleccionarlo; SoundDeck actualiza
  la lista al recibir cambios de audio de Windows.
