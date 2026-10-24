# Práctica 3: animaciones del jugador

La práctica de búsqueda de objetos está documentada en
[PRACTICA3-COLECCIONABLES.md](PRACTICA3-COLECCIONABLES.md).

Proyecto: Unity **6000.6.4f1**. Escena: `Assets/Scenes/nivel1.unity`.

## Ejecutar

Abre el proyecto en Unity, abre `nivel1` y pulsa **Play**.

- **A / flecha izquierda**: caminar a la izquierda.
- **D / flecha derecha**: caminar a la derecha.
- **Espacio**: saltar cuando el jugador está apoyado en el suelo.

## Configuración de la práctica

| Requisito | Configuración |
| --- | --- |
| División y tamaño de sprites | Las seis poses del zombi usan Sprite Mode **Multiple**, un recorte de **80 × 110 px** por PNG y **100 Pixels Per Unit**. |
| Alineación | Pivote común en **(40, 45) px**; las poses conservan la misma referencia para los pies. |
| Sprite por defecto | `zombie_idle_0` en el SpriteRenderer del jugador. |
| Colisión | **BoxCollider2D** de **0.62 × 0.9** unidades y offset **(0, 0)**, ajustado al cuerpo en reposo. El collider conserva su tamaño durante las animaciones. |
| Material físico | `JugadorSinFriccion`: fricción **0**, rebote **0** y combinación **Minimum**, asignado al BoxCollider2D del jugador. Evita quedar suspendido al mantener la dirección contra los laterales y esquinas de los pisos. |
| Altura del salto | `velocidadSalto = 8.5` con gravedad **2**. Permite alcanzar la primera plataforma, que está **1.65** unidades sobre el suelo. |
| Idle | Estado inicial del Animator. Alterna stand/idle a **2.5 fps**, ciclo de **0.8 s**. Su archivo conserva el nombre `Quieto.anim`. |
| Caminar | Alterna walk1/walk2 a **8 fps**, ciclo de **0.25 s**. |
| Salto | Jump al inicio y fall a **0.4 s**, clip de **0.6 s** a **5 fps**, sin repetición. Mantiene la última pose mientras continúa en el aire. |
| Animator | Una capa con **Idle**, **Caminar** y **Salto**. Seis transiciones, todas con **Has Exit Time = false** y **Transition Duration = 0**. |
| Código | `MovimientoJugador.cs` actualiza los bool **Caminando** y **Saltando**, orienta el sprite y permite saltar solo desde una superficie de apoyo. |
| Escena del juego | `nivel1` queda habilitada como escena inicial de compilación. |

`Saltando` tiene prioridad mientras sube, cae o abandona una plataforma. Al aterrizar,
el jugador vuelve a Idle o Caminar según las teclas. Los contactos con paredes o
techos no cuentan como suelo.

## Verificación

El menú **Práctica 3 → Validar** ejecuta la comprobación de recursos, transiciones,
entrada y física. Detén Play y guarda o cierra las escenas sin título antes de
ejecutar el menú. También se puede ejecutar Unity en batch con
`-executeMethod ValidarPractica3.Run`. La consola muestra el resultado y el proceso
devuelve un código distinto de cero si falla una comprobación.

Resultado de la verificación con Unity 6000.6.4f1: **144 comprobaciones, 0 errores**,
incluyendo recursos, controles, salto, caída, aterrizaje, borde, caída al presionar
ambos laterales y ambas esquinas de una plataforma real, y acceso al primer piso
con la geometría real de `Suelo` y `Plataforma1`.

Los sprites son de **Kenney, Platformer Characters 1**, bajo licencia **CC0**.
Se incluye el texto original en `Assets/Sprites/Kenney-License.txt`.
