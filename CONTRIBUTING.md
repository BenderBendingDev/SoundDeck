# Contribuir a SoundDeck

1. Crea una rama corta desde `main`, por ejemplo `feature/midi-learn`.
2. Mantén la lógica de dominio en `SoundDeck.Core` y las dependencias de
   Windows detrás de sus interfaces.
3. Ejecuta `dotnet build SoundDeck.sln` y `dotnet test SoundDeck.sln`.
4. No subas sonidos, bases de datos, certificados ni secretos.
5. Abre un pull request describiendo el cambio y cómo se verificó.

Los cambios de audio deben contemplar dispositivos ausentes o desconectados,
evitar bloquear el hilo de interfaz y conservar los originales importados.
