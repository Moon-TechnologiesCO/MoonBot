# Decisiones de MoonBot

## 1. Estado inicial de la máquina de estados

**Decisión:** Al arrancar, MoonBot está en `Idle`.

**Por qué:** Quien abre MoonBot quiere usarlo ahora, así que debe estar despierto y listo desde el primer segundo.

**Alternativa descartada:** `Sleeping`. Si arrancara dormido, el usuario tendría que despertarlo antes de poder usarlo, un paso extra sin ningún beneficio. `Sleeping` lo reservamos para cuando MoonBot lleve un rato sin actividad.