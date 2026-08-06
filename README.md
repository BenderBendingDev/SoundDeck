# SoundDeck

Panel de sonidos nativo para Windows 10/11. Reproduce archivos locales por tus
auriculares, por VB-CABLE o por ambas salidas, y mezcla el micrófono para que
Discord y los juegos reciban voz y efectos desde una sola entrada.

## Funciones

- Tableros, categorías, búsqueda y botones reordenables.
- Importación de WAV, MP3, FLAC, OGG, M4A y AAC a una biblioteca local.
- Recorte no destructivo, fundidos, forma de onda, ganancia y normalización.
- Ruta configurable por sonido: local, VB-CABLE o ambas.
- Mezcla continua del micrófono, mute, volumen y medidor de nivel.
- Atajos globales y asignaciones MIDI con modo de aprendizaje.
- Bandeja del sistema, inicio opcional con Windows y copias de seguridad.
- Persistencia SQLite en `%LocalAppData%\SoundDeck`.

## Requisitos

- Windows 10 1903 (build 18362) o posterior; Windows 11 recomendado.
- [VB-CABLE](https://vb-audio.com/Cable/) para la salida virtual.
- Para desarrollar: SDK de .NET 10 y Visual Studio 2026 con herramientas de
  aplicaciones Windows, o la CLI de .NET.

## Configuración rápida

1. Instala VB-CABLE como administrador y reinicia Windows.
2. Abre SoundDeck y selecciona tu micrófono, salida local y `CABLE Input`.
3. En Discord o el juego, selecciona `CABLE Output (VB-Audio Virtual Cable)`
   como dispositivo de entrada.
4. Añade un archivo y elige si sale por local, VB-CABLE o ambas.

Consulta la [guía detallada de VB-CABLE](docs/configuracion-vb-cable.md).

## Desarrollo

```powershell
dotnet restore
dotnet build SoundDeck.sln
dotnet test SoundDeck.sln
dotnet run --project src/SoundDeck.App/SoundDeck.App.csproj
```

La base de datos, preferencias y biblioteca no se guardan en el repositorio.
Los originales importados se copian y nunca se modifican: el recorte, los
fundidos y la ganancia se aplican durante la reproducción.

## Crear el instalador

```powershell
./scripts/package.ps1
```

El paquete personal se genera sin certificado público en `artifacts/msix`.
Para distribuirlo públicamente hay que firmarlo con un certificado de confianza
y guardar sus credenciales como secretos de GitHub, nunca en el repositorio.

## Estructura

- `src/SoundDeck.App`: WinUI 3, navegación e integración con Windows.
- `src/SoundDeck.Core`: dominio y contratos.
- `src/SoundDeck.Audio`: WASAPI, mezcla, reproducción, MIDI y atajos.
- `src/SoundDeck.Infrastructure`: SQLite, biblioteca, copias y arranque.
- `tests/SoundDeck.Tests`: pruebas sin dependencia de hardware.

## Licencia

[MIT](LICENSE)
