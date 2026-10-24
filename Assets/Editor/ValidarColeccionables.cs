using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Ejecutar: Unity -batchmode -projectPath ... -executeMethod ValidarColeccionables.Run -logFile ...
// Comprueba los assets y la escena reales. La logica del trigger se invoca sobre
// clones temporales en Edit Mode; esta prueba no afirma callbacks fisicos de Play Mode.
public static class ValidarColeccionables
{
    private const string ScenePath = "Assets/Scenes/nivel1.unity";
    private const string SpriteFolder = "Assets/Sprites/Coleccionables/";
    private static readonly string[] Names = { "Cake", "Coffee", "Cookie", "Burger", "Candy" };
    private static readonly List<string> errors = new List<string>();
    private static int checks;

    [MenuItem("Práctica 3/Validar coleccionables")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Deten Play antes de validar los coleccionables.");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        checks = 0;
        errors.Clear();
        try
        {
            ValidateScene();
        }
        catch (Exception ex)
        {
            errors.Add("Excepcion durante la validacion: " + ex);
        }

        foreach (string error in errors) Debug.LogError("COLECCIONABLES FAIL: " + error);
        string summary = $"COLECCIONABLES {(errors.Count == 0 ? "PASS" : "FAIL")}: {checks} comprobaciones, {errors.Count} errores.";
        if (errors.Count == 0) Debug.Log(summary);
        else Debug.LogError(summary);
        if (Application.isBatchMode) EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
    }

    private static void Check(bool passed, string description)
    {
        checks++;
        if (!passed) errors.Add(description);
    }

    private static T[] Components<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    }

    private static void ValidateScene()
    {
        Check(UnityEditorInternal.InternalEditorUtility.tags.Contains("Collectible"), "Existe el tag Collectible.");
        int layer = LayerMask.NameToLayer("Collectibles");
        Check(layer >= 8, "Existe una capa de usuario llamada Collectibles.");
        Check(AssetDatabase.IsValidFolder(SpriteFolder.TrimEnd('/')), "Existe Assets/Sprites/Coleccionables.");

        // Batch comienza con una escena vacia sin guardar, sin trabajo de usuario.
        if (Application.isBatchMode) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        for (int index = 0; index < SceneManager.sceneCount; index++)
            if (string.IsNullOrEmpty(SceneManager.GetSceneAt(index).path))
                throw new InvalidOperationException("Guarda o cierra las escenas sin titulo antes de validar.");

        Scene previousActive = SceneManager.GetActiveScene();
        Scene sourceScene = SceneManager.GetSceneByPath(ScenePath);
        bool openedSource = !sourceScene.isLoaded;
        Scene testScene = default;
        try
        {
            if (openedSource) sourceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            Transform[] transforms = Components<Transform>(sourceScene);
            MovimientoJugador[] players = Components<MovimientoJugador>(sourceScene);
            Check(players.Length == 1, "nivel1 contiene exactamente un jugador con MovimientoJugador.");
            if (players.Length != 1) return;
            MovimientoJugador player = players[0];
            Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            Check(body != null && body.simulated && body.bodyType == RigidbodyType2D.Dynamic,
                "El jugador tiene un Rigidbody2D dinamico y simulado para recibir triggers.");
            Collider2D playerCollider = player.GetComponent<Collider2D>();
            Check(playerCollider != null && playerCollider.enabled && !playerCollider.isTrigger,
                "El collider del jugador sigue activo y solido.");
            if (layer >= 0)
                Check(!Physics2D.GetIgnoreLayerCollision(player.gameObject.layer, layer),
                    "La matriz de fisica permite contactos entre el jugador y Collectibles.");

            var collectibles = new List<GameObject>();
            var sprites = new HashSet<Sprite>();
            foreach (string name in Names)
            {
                GameObject[] found = transforms.Where(item => item.name == name).Select(item => item.gameObject).ToArray();
                Check(found.Length == 1, "Existe exactamente un objeto " + name + ".");
                if (found.Length != 1) continue;
                GameObject item = found[0];
                collectibles.Add(item);
                Check(item.activeInHierarchy, name + ": comienza activo.");
                Check(item.tag == "Collectible", name + ": tag Collectible.");
                Check(layer >= 0 && item.layer == layer, name + ": layer Collectibles.");
                BoxCollider2D box = item.GetComponent<BoxCollider2D>();
                Check(box != null && box.enabled && box.isTrigger && box.size.x > 0 && box.size.y > 0,
                    name + ": BoxCollider2D activo, de tamano valido y con Is Trigger.");
                SpriteRenderer renderer = item.GetComponent<SpriteRenderer>();
                Sprite sprite = renderer != null ? renderer.sprite : null;
                Check(renderer != null && renderer.enabled && sprite != null,
                    name + ": SpriteRenderer con sprite asignado.");
                if (sprite != null)
                {
                    sprites.Add(sprite);
                    string spritePath = AssetDatabase.GetAssetPath(sprite);
                    Check(spritePath.StartsWith(SpriteFolder, StringComparison.Ordinal),
                        name + ": sprite guardado dentro de Assets/Sprites/Coleccionables.");
                    TextureImporter importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;
                    Check(importer != null && importer.textureType == TextureImporterType.Sprite,
                        name + ": textura importada como Sprite 2D.");
                    Check(sprite.rect.width > 0 && sprite.rect.height > 0 && sprite.pixelsPerUnit > 0,
                        name + ": dimensiones y pixeles por unidad validos.");
                }
            }
            Check(sprites.Count == Names.Length, "Los cinco coleccionables utilizan sprites distintos.");
            Check(transforms.Count(item => item.gameObject.tag == "Collectible") == Names.Length,
                "El nivel comienza con exactamente cinco objetos con tag Collectible.");

            Camera[] cameras = Components<Camera>(sourceScene).Where(camera => camera.CompareTag("MainCamera")).ToArray();
            Check(cameras.Length == 1 && cameras[0].enabled, "Existe una Main Camera activa.");
            TextMeshProUGUI[] counters = Components<TextMeshProUGUI>(sourceScene)
                .Where(text => text.name == "ContadorJuego").ToArray();
            Check(counters.Length == 1, "Existe exactamente un TextMeshProUGUI llamado ContadorJuego.");
            if (counters.Length != 1) return;
            TextMeshProUGUI counter = counters[0];
            Canvas canvas = counter.GetComponentInParent<Canvas>();
            Check(canvas != null && canvas.enabled && canvas.renderMode == RenderMode.ScreenSpaceCamera,
                "El Canvas de ContadorJuego usa Screen Space - Camera.");
            Check(canvas != null && cameras.Length == 1 && canvas.worldCamera == cameras[0],
                "El Canvas esta vinculado a Main Camera.");
            Check(counter.enabled && counter.gameObject.activeInHierarchy && counter.font != null && counter.fontSize > 0,
                "El contador esta activo y tiene fuente TMP y tamano legible.");
            Check(counter.text == "Objetos: 0 / 5", "El texto inicial es Objetos: 0 / 5.");
            var serializedPlayer = new SerializedObject(player);
            SerializedProperty counterProperty = serializedPlayer.FindProperty("contadorJuego");
            Check(counterProperty != null && counterProperty.propertyType == SerializedPropertyType.ObjectReference &&
                counterProperty.objectReferenceValue == counter, "MovimientoJugador tiene contadorJuego serializado y vinculado al TMP del Canvas.");

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo awake = typeof(MovimientoJugador).GetMethod("Awake", flags);
            MethodInfo enter = typeof(MovimientoJugador).GetMethod("OnTriggerEnter2D", flags, null, new[] { typeof(Collider2D) }, null);
            FieldInfo count = typeof(MovimientoJugador).GetField("objetosRecogidos", flags);
            Check(awake != null && enter != null, "El controlador implementa Awake y OnTriggerEnter2D(Collider2D).");
            Check(count != null && count.FieldType == typeof(int), "El controlador conserva objetosRecogidos como contador entero.");
            if (awake == null || enter == null || count == null || canvas == null || counterProperty == null ||
                collectibles.Count != Names.Length || layer < 8)
                return;

            testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(testScene);
            GameObject clonePlayer = Object.Instantiate(player.gameObject);
            clonePlayer.name = "Jugador_ValidacionColeccionables";
            SceneManager.MoveGameObjectToScene(clonePlayer, testScene);
            MovimientoJugador movement = clonePlayer.GetComponent<MovimientoJugador>();
            GameObject cloneCanvas = Object.Instantiate(canvas.gameObject);
            cloneCanvas.name = "Canvas_ValidacionColeccionables";
            SceneManager.MoveGameObjectToScene(cloneCanvas, testScene);
            TextMeshProUGUI cloneCounter = cloneCanvas.GetComponentsInChildren<TextMeshProUGUI>(true)
                .Single(text => text.name == "ContadorJuego");
            var serializedClone = new SerializedObject(movement);
            serializedClone.FindProperty("contadorJuego").objectReferenceValue = cloneCounter;
            serializedClone.ApplyModifiedPropertiesWithoutUndo();
            awake.Invoke(movement, null);
            Check((int)count.GetValue(movement) == 0 && cloneCounter.text == "Objetos: 0 / 5",
                "Awake inicializa el contador y su etiqueta en cero.");

            GameObject unrelated = new GameObject("ObjetoNoColeccionable_Validacion");
            SceneManager.MoveGameObjectToScene(unrelated, testScene);
            unrelated.layer = layer;
            BoxCollider2D unrelatedCollider = unrelated.AddComponent<BoxCollider2D>();
            unrelatedCollider.isTrigger = true;
            enter.Invoke(movement, new object[] { unrelatedCollider });
            Check(unrelated.activeSelf && (int)count.GetValue(movement) == 0 && cloneCounter.text == "Objetos: 0 / 5",
                "Un trigger en Collectibles pero sin tag Collectible se ignora sin destruirlo ni sumar.");
            unrelated.tag = "Collectible";
            unrelated.layer = 0;
            enter.Invoke(movement, new object[] { unrelatedCollider });
            Check(unrelated.activeSelf && (int)count.GetValue(movement) == 0 && cloneCounter.text == "Objetos: 0 / 5",
                "Un trigger con tag Collectible pero fuera de la capa Collectibles se ignora.");

            int collected = 0;
            foreach (GameObject sourceItem in collectibles)
            {
                GameObject item = Object.Instantiate(sourceItem);
                item.name = sourceItem.name + "_Validacion";
                SceneManager.MoveGameObjectToScene(item, testScene);
                BoxCollider2D box = item.GetComponent<BoxCollider2D>();
                enter.Invoke(movement, new object[] { box });
                collected++;
                Check(!item.activeSelf, sourceItem.name + ": la recoleccion desactiva el objeto inmediatamente.");
                Check((int)count.GetValue(movement) == collected && cloneCounter.text == $"Objetos: {collected} / 5",
                    sourceItem.name + ": suma exactamente una unidad y actualiza el TMP.");
                // Destroy se difiere hasta el final del frame en Play Mode. Simular
                // otro aviso del mismo collider comprueba la proteccion contra doble conteo.
                enter.Invoke(movement, new object[] { box });
                Check((int)count.GetValue(movement) == collected && cloneCounter.text == $"Objetos: {collected} / 5",
                    sourceItem.name + ": un aviso repetido no vuelve a contar el objeto inactivo.");
            }
            Check((int)count.GetValue(movement) == Names.Length && cloneCounter.text == "Objetos: 5 / 5",
                "Recoger los cinco objetos completa el contador en 5 / 5.");
            Check(collectibles.All(item => item.activeSelf) && counter.text == "Objetos: 0 / 5",
                "Las pruebas mantienen intactos los objetos y el contador de la escena original.");
            Debug.Log("COLECCIONABLES: assets, escena y logica del trigger sobre clones verificados; no se simulan callbacks fisicos de Play Mode.");
        }
        finally
        {
            try
            {
                if (testScene.IsValid() && testScene.isLoaded) EditorSceneManager.CloseScene(testScene, true);
            }
            finally
            {
                try
                {
                    if (openedSource && sourceScene.IsValid() && sourceScene.isLoaded) EditorSceneManager.CloseScene(sourceScene, true);
                }
                finally
                {
                    if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                }
            }
        }
    }
}
