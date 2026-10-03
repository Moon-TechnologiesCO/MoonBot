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