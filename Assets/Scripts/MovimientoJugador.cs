using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
[RequireComponent(typeof(Animator), typeof(SpriteRenderer))]
public class MovimientoJugador : MonoBehaviour
{
    [SerializeField] private float velocidad = 5f;
    [SerializeField] private float velocidadSalto = 8.5f;
    [SerializeField] private TextMeshProUGUI contadorJuego;
    [SerializeField, Min(1)] private int totalColeccionables = 5;

    private int objetosRecogidos;

    private static readonly int Caminando = Animator.StringToHash("Caminando");
    private static readonly int Saltando = Animator.StringToHash("Saltando");

    private Rigidbody2D cuerpo;
    private Animator animador;
    private SpriteRenderer imagenJugador;

    private float direccionHorizontal;
    private bool saltoSolicitado;

    private ContactFilter2D filtroSuelo;

    private void Awake()
    {
        cuerpo = GetComponent<Rigidbody2D>();
        animador = GetComponent<Animator>();
        imagenJugador = GetComponent<SpriteRenderer>();

        // detectar superficies que sostienen(prueba15 jjj) al jugador desde abajo.
        filtroSuelo = new ContactFilter2D();
        filtroSuelo.useTriggers = false;
        filtroSuelo.SetNormalAngle(45f, 135f);

        objetosRecogidos = 0;
        ActualizarContador();
    }

    private void Update()
    {
        direccionHorizontal = 0f;

        Keyboard teclado = Keyboard.current;

        if (teclado == null)
        {
            saltoSolicitado = false;
            return;
        }

        // Movimiento hacia la izquierda.
        if (teclado.aKey.isPressed || teclado.leftArrowKey.isPressed)
        {
            direccionHorizontal -= 1f;
        }

        // Movimiento hacia la derecha.
        if (teclado.dKey.isPressed || teclado.rightArrowKey.isPressed)
        {
            direccionHorizontal += 1f;
        }

        // Guardar la solicitud de salto.
        if (teclado.spaceKey.wasPressedThisFrame)
        {
            saltoSolicitado = true;
        }

        bool estaCaminando = direccionHorizontal != 0f;

        // Orientar la imagen sin girar el cuerpo fisico.
        // Asumimos que la imagen original mira hacia la derecha.
        if (estaCaminando)
        {
            imagenJugador.flipX = direccionHorizontal < 0f;
        }
    }

    private void FixedUpdate()
    {
        Vector2 velocidadActual = cuerpo.linearVelocity;

        // Movimiento horizontal por ahora si .
        velocidadActual.x = direccionHorizontal * velocidad;

        // Comprobar si una superficie sostiene al personaje.
        bool enSuelo = cuerpo.IsTouching(filtroSuelo);

        // Saltar solamente estando apoyado  sin estar subiendo.
        if (saltoSolicitado && enSuelo && velocidadActual.y <= 0.1f)
        {
            velocidadActual.y = velocidadSalto;
            // Los contactos todavia corresponden al paso fisico anterior.
            enSuelo = false;
        }

        cuerpo.linearVelocity = velocidadActual;

        // Salto tiene prioridad al subir, caer o abandonar una plataforma.
        bool estaSaltando = !enSuelo || velocidadActual.y > 0.1f;
        animador.SetBool(Saltando, estaSaltando);
        animador.SetBool(Caminando, !estaSaltando && direccionHorizontal != 0f);

        // Cada pulsacion se procesa una sola vez y nitido .
        saltoSolicitado = false;
    }

    private void OnTriggerEnter2D(Collider2D otro)
    {
        if (otro == null || !otro.gameObject.activeInHierarchy ||
            !otro.CompareTag("Collectible") ||
            otro.gameObject.layer != LayerMask.NameToLayer("Collectibles"))
        {
            return;
        }

        // Desactivar de inmediato evita contar dos contactos del mismo objeto.
        otro.gameObject.SetActive(false);
        objetosRecogidos++;
        ActualizarContador();

        if (Application.isPlaying)
        {
            Destroy(otro.gameObject);
        }
    }

    private void ActualizarContador()
    {
        if (contadorJuego != null)
        {
            contadorJuego.text = $"Objetos: {objetosRecogidos} / {totalColeccionables}";
        }
    }
}
