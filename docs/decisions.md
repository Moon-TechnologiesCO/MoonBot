# Decisiones de MoonBot

## 1. Estado inicial de la máquina de estados

**Decisión:** Al arrancar, MoonBot está en `Idle`.

**Por qué:** Quien abre MoonBot quiere usarlo ahora, así que debe estar despierto y listo desde el primer segundo.

**Alternativa descartada:** `Sleeping`. Si arrancara dormido, el usuario tendría que despertarlo antes de poder usarlo, un paso extra sin ningún beneficio. `Sleeping` lo reservamos para cuando MoonBot lleve un rato sin actividad.


## 2. Cómo reaccionan las transiciones ilegales

**Decisión:** `TryChangeTo` devuelve `bool`: `true` si el cambio se hizo, `false` si no estaba permitido. No lanza excepciones.

**Por qué:** Los cambios de estado vienen de eventos externos (clics, temporizadores, respuestas de la IA que llegan tarde). Un cambio fuera de orden es normal y no debe cerrar la aplicación.

**Alternativa descartada:** Lanzar `InvalidOperationException`. Obligaría a rodear cada cambio con `try/catch`, y cualquier descuido tumbaría MoonBot por un caso normal.

**Riesgo aceptado:** Alguien puede ignorar el `false` y esconder un bug. Pendiente: registrar en el log cada transición rechazada cuando exista el logging.

## 3. Framework de UI: Avalonia 12 (confirmado por spike)

**Decisión:** Se queda Avalonia 12.

**Por qué:** El spike del 20 de octubre de 2026 comprobó en Windows 11, con dos monitores a escalas distintas (125 % y 100 %):

- Ventana sin bordes y transparente: se ve el escritorio detrás de la luna (`WindowDecorations="None"`, `TransparencyLevelHint="Transparent"`).
- Siempre encima: Win32 reporta `WS_EX_TOPMOST`.
- No roba el foco al abrirse (`ShowActivated="False"`): la ventana activa sigue siendo la misma antes y después.
- Dibujo propio a unos 60 fps con `DrawingContext` (Skia), con recorte por geometría para la sombra de fase lunar.

**Alternativa descartada:** Volver a WPF. Funcionaría, pero solo en Windows, y el spike no encontró ningún bloqueo que lo justifique.

**Limitaciones conocidas (requieren Win32, futuro `MoonBot.Platform.Windows`):**

- Probablemente los clics sobre las zonas transparentes no atraviesan la ventana hacia la app de atrás (sin verificar todavía).
- Avalonia solo conoce la posición del cursor dentro de su ventana. Para que los ojos sigan al cursor por toda la pantalla hace falta `GetCursorPos`.
- `Position` hay que asignarlo antes de mostrar la ventana. En `Opened` se ignora y Windows la coloca en el monitor del cursor.


## 4. Proveedores de IA detrás de una interfaz

**Decisión:** `Core` define `IAiProvider` (respuesta por streaming y lista de modelos). La v0.2 trae dos implementaciones: Ollama (local, por defecto) y "compatible con OpenAI" (nube o LM Studio).

**Por qué:** MoonBot no debe depender de un solo proveedor. `ChatSession` y sus tests no saben si hablan con Ollama, con la nube o con un falso.

**Modelo por defecto:** `gemma3:4b`. En la prueba del 20 de octubre de 2026, `qwen3:4b` escribió su razonamiento dentro de la respuesta incluso con `think:false` y tardó 28 s en un saludo; `gemma3:4b` respondió directo.

**Pendiente:** el proveedor de Anthropic se añadirá en la etapa 4 con su SDK oficial de C#, después de revisar su licencia.


## 5. Una API key solo viaja por HTTPS o dentro de este PC

**Decisión:** `AiProviderFactory` rechaza (`AiErrorKind.InsecureKey`) cualquier combinación de clave + `http://` hacia otra máquina.

**Por qué:** por HTTP la clave viaja en texto plano y cualquiera en la misma red (por ejemplo, un wifi público) puede leerla. `http://localhost` es aceptable porque el tráfico no sale del equipo.


## 6. Idiomas en JSON incrustado, inglés por defecto

**Decisión:** 8 idiomas (en, es, pt, fr, de, it, zh, ja) en `src/MoonBot.Core/Localization/strings-*.json`. El inglés es el predeterminado y la referencia. Un test exige que cada idioma tenga todas las claves y los mismos `{0}`.

**Por qué no `.resx`:** el JSON lo puede editar un traductor sin Visual Studio, y el test detecta frases olvidadas.

**Riesgo aceptado:** las traducciones las escribió el asistente. Antes de anunciar un idioma, debe revisarlo un hablante nativo.


## 7. Agente con herramientas de archivos, permisos y sin borrar

**Decisión:** el modelo puede buscar, listar, crear carpetas, renombrar y mover dentro de las carpetas personales del usuario (`FileSandbox`). Todo pasa por `ToolRouter`: falla cerrado, pide permiso para cualquier cambio, tiene un límite de tiempo y deja auditoría. No existe herramienta de borrar.

**Por qué:** la frase "nunca permitas que el modelo ejecute comandos arbitrarios" del plan inicial. Un modelo pequeño se equivoca; sin herramienta de borrar, el peor caso es un archivo movido, que se puede deshacer.

**Riesgo aceptado:** los modelos locales de 4B no siempre eligen la herramienta (en el test real del 20 de octubre de 2026, `nemotron-3-nano:4b` falló una de dos veces al crear una carpeta). Por eso existe `/find`, que no depende de la IA.


## 8. Dictado con Ctrl derecho, sin hook de teclado y con Whisper local

**Decisión:** se consulta cada 30 ms el estado de una sola tecla (`GetAsyncKeyState`) en lugar de instalar un hook global; el audio se transcribe en el PC con whisper.cpp; el texto se escribe con `SendInput` Unicode.

**Por qué:** un hook de teclado ve todas las pulsaciones, incluidas contraseñas; consultar una tecla no. La voz es un dato personal: no debe salir del equipo. `SendInput` no toca el portapapeles del usuario.

**Alternativas descartadas:** el reconocimiento de voz de Windows (`Windows.Media.SpeechRecognition`) para dictado usa el servicio en línea de Microsoft; System.Speech (SAPI) es local pero con muy baja calidad.

**Límites conocidos:** Windows no permite escribir en ventanas abiertas como administrador (UIPI); si el usuario cambia de ventana mientras se transcribe, el texto va al chat de MoonBot en vez de a la ventana nueva.
