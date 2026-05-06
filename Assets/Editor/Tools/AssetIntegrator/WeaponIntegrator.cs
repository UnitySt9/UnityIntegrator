using System.IO;
using UnityEditor;
using UnityEngine;

namespace Editor.Tools.AssetIntegrator
{
    public class WeaponIntegrator : IIntegrationStep
    {
        // === Конфигурация ===
        private const string CombinedFbxPath = "Assets/_Project/Art/Weapons/FBX/CZ600.fbx"; // Единый FBX с оружием и прицелом
        
        private const string WeaponRootName = "CZ600_Base";     // Имя корневого объекта оружия в FBX
        private const string OpticRootName = "CZ600_Optic";     // Имя корневого объекта прицела в FBX
        
        private const string WeaponBasePrefabPath = "Assets/_Project/Prefabs/Weapons/Stock/CZ600_Base.prefab";
        private const string OpticPrefabPath = "Assets/_Project/Prefabs/Weapons/Attachments/CZ600_Optic.prefab";
        private const string CombinedPrefabPath = "Assets/_Project/Prefabs/Weapons/Assembled/CZ600_Base_WithOptic.prefab";
        
        // === Состояние ===
        public string StepName => "Интеграция оружия и прицела";
        public StepStatus Status { get; private set; } = StepStatus.Pending;
        public string Log { get; private set; } = "";
        public bool IsEnabled { get; set; } = true;
        public bool AutoFix { private get; set; } = false;

        private void AddLog(string msg, bool isError = false)
        {
            Log += (isError ? "[ERROR] " : "[OK] ") + msg + "\n";
            if (isError)
            {
                Debug.LogError(msg);
                Status = StepStatus.Error;
            }
            else if (Status != StepStatus.Error) Status = StepStatus.Success;
        }

        public void Validate()
        {
            Status = StepStatus.Pending;
            Log = "";
            AddLog("--- Проверка интеграции Оружия ---");

            // 1. Проверка папок
            EnsureFolder("Assets/_Project/Art/Weapons/FBX");
            EnsureFolder("Assets/_Project/Art/Weapons/Materials/CZ600");
            EnsureFolder("Assets/_Project/Art/Weapons/Textures");
            EnsureFolder("Assets/_Project/Prefabs/Weapons/Stock");
            EnsureFolder("Assets/_Project/Prefabs/Weapons/Attachments");
            EnsureFolder("Assets/_Project/Prefabs/Weapons/Assembled");

            // 2. Валидация импорта FBX и настройка
            ValidateAndConfigureFBXImport();

            // 3. Создание/валидация шейдера
            var shader = ValidateOrCreateShader();

            // 4. Создание/валидация материалов с привязкой текстур
            ValidateOrCreateMaterial("CZ600_weapon_base_mat", shader);
            ValidateOrCreateMaterial("CZ600_optic_mat", shader);

            // 5. Проверка префабов
            ValidateWeaponBasePrefab();
            ValidateOpticPrefab();
            ValidateCombinedPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            if(Status != StepStatus.Error) AddLog("Проверка оружия завершена.");
        }

        public void ApplyFix()
        {
            if (Status != StepStatus.Error) return;
            AutoFix = true;
            Log = "";
            AddLog("--- Запуск автоисправлений для оружия ---");
            Validate();
            AutoFix = false;
        }

        public void Reset() => Status = StepStatus.Pending;

        // --- Поиск корневых объектов в общем FBX ---
        private Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private GameObject ExtractSubAssetFromFBX(string rootObjectName)
        {
            var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CombinedFbxPath);
            if (fbxAsset == null)
            {
                AddLog($"FBX не найден по пути: {CombinedFbxPath}", true);
                return null;
            }

            var rootTransform = fbxAsset.transform;
            var targetTransform = FindChildRecursive(rootTransform, rootObjectName);
            
            if (targetTransform == null)
            {
                AddLog($"Объект '{rootObjectName}' не найден в FBX. Проверьте иерархию.", true);
                return null;
            }

            // Создаём временный GameObject только с нужной веткой
            var extractedRoot = new GameObject(targetTransform.name);
            CopyTransformHierarchy(targetTransform, extractedRoot.transform);
            return extractedRoot;
        }

        private void CopyTransformHierarchy(Transform source, Transform destination)
        {
            // Копируем компоненты (MeshRenderer, MeshFilter и т.д.)
            foreach (var component in source.GetComponents<Component>())
            {
                // Transform и дочерние компоненты копируем отдельно
                if (component is Transform || component is Animator || component is Animation)
                    continue;
                    
                UnityEditorInternal.ComponentUtility.CopyComponent(component);
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(destination.gameObject);
            }

            // Рекурсивно копируем детей
            foreach (Transform child in source)
            {
                var childCopy = new GameObject(child.name);
                childCopy.transform.SetParent(destination);
                CopyTransformHierarchy(child, childCopy.transform);
            }
        }

        // --- FBX Импорт ---
        private void ValidateAndConfigureFBXImport()
        {
            if (!File.Exists(CombinedFbxPath))
            {
                AddLog($"FBX не найден по пути: {CombinedFbxPath}", true);
                return;
            }

            var importer = AssetImporter.GetAtPath(CombinedFbxPath) as ModelImporter;
            if (importer == null)
            {
                AddLog($"Не удалось получить ModelImporter для {CombinedFbxPath}", true);
                return;
            }

            bool needsReimport = false;

            // 1. Отключаем генерацию материалов
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                AddLog("FBX импортирует материалы. Отключаем...");
                if (AutoFix)
                {
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    needsReimport = true;
                }
            }

            // 2. Проверяем настройку рига
            if (importer.animationType != ModelImporterAnimationType.None)
            {
                AddLog("Отключаем анимации для оружия...");
                if (AutoFix)
                {
                    importer.animationType = ModelImporterAnimationType.None;
                    needsReimport = true;
                }
            }
            else
            {
                AddLog("Настройки импорта FBX корректны.");
            }

            // 3. Убедимся, что FBX импортируется с читаемыми мешами (для извлечения подобъектов)
            if (!importer.isReadable)
            {
                AddLog("Включаем Read/Write для FBX...");
                if (AutoFix)
                {
                    importer.isReadable = true;
                    needsReimport = true;
                }
            }

            if (needsReimport)
            {
                importer.SaveAndReimport();
                AddLog("Настройки FBX обновлены и применены.");
            }
        }

        // --- Шейдер ---
        private Shader ValidateOrCreateShader()
        {
            Shader shader = Shader.Find("Shader Graphs/Weapon");
            if (shader == null)
            {
                AddLog("Целевой шейдер 'Shader Graphs/Weapon' не найден.", true);
                if (AutoFix)
                {
                    AddLog("Создаём шейдер-заглушку Shader Graphs/Weapon...");
                    string shaderCode = @"
Shader ""Shader Graphs/Weapon""
{
    Properties
    {
        _BaseMap (""Base Map"", 2D) = ""white"" {}
        _Metallic (""Metallic"", Range(0,1)) = 0
        _Smoothness (""Smoothness"", Range(0,1)) = 0.5
        _BumpMap (""Normal Map"", 2D) = ""bump"" {}
        _MetallicGlossMap (""Metallic(R) Smoothness(A)"", 2D) = ""white"" {}
    }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" ""RenderPipeline""=""UniversalPipeline"" }
        LOD 200
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
            struct Attributes { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            Texture2D _BaseMap; SamplerState sampler_BaseMap;
            float _Metallic, _Smoothness;
            Varyings vert(Attributes v) { Varyings o; o.pos = TransformObjectToHClip(v.pos.xyz); o.uv = v.uv; return o; }
            half4 frag(Varyings i) : SV_Target { return half4(0.6,0.55,0.5,1); }
            ENDHLSL
        }
    }
}";
                    string fullPath = "Assets/_Project/Art/Shaders/Weapon.shader";
                    EnsureFolder(Path.GetDirectoryName(fullPath));
                    File.WriteAllText(fullPath, shaderCode);
                    AssetDatabase.Refresh();
                    shader = Shader.Find("Shader Graphs/Weapon");
                    if (shader != null) AddLog("Шейдер-заглушка успешно создан.");
                }
            }
            else AddLog("Шейдер Shader Graphs/Weapon найден.");
            return shader;
        }

        // --- Материалы с текстурами ---
        private void ValidateOrCreateMaterial(string matName, Shader shader)
        {
            string matPath = $"Assets/_Project/Art/Weapons/Materials/CZ600/{matName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (material == null)
            {
                AddLog($"Материал {matName} не найден.", true);
                if (AutoFix)
                {
                    material = new Material(shader ?? Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(material, matPath);
                    AddLog($"Материал {matName} создан.");
                }
                else return;
            }
            else AddLog($"Материал {matName} найден.");

            // Привязка текстур
            var fbxDir = Path.GetDirectoryName(CombinedFbxPath);
            var texDir = fbxDir.Replace("/FBX", "/Textures");
            
            AssignTextureBySuffix(material, "_BaseMap", FindTextureBySuffix(texDir, "_d", "_BaseColor", "_Albedo"));
            AssignTextureBySuffix(material, "_BumpMap", FindTextureBySuffix(texDir, "_n", "_Normal"), true);
            AssignTextureBySuffix(material, "_MetallicGlossMap", FindTextureBySuffix(texDir, "_mg", "_Metallic", "_MetallicGloss"));
        }

        private Texture2D FindTextureBySuffix(string directory, params string[] suffixes)
        {
            if (!AssetDatabase.IsValidFolder(directory)) return null;
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { directory });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path);
                foreach (var suffix in suffixes)
                    if (fileName.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }

        private void AssignTextureBySuffix(Material mat, string property, Texture2D tex, bool isNormalMap = false)
        {
            if (tex == null) return;
            if (mat.GetTexture(property) == tex) return;

            AddLog($"Привязываю текстуру {tex.name} к свойству {property} материала {mat.name}.");
            if (AutoFix)
            {
                mat.SetTexture(property, tex);
                if (isNormalMap)
                {
                    var texImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
                    if (texImporter != null && texImporter.textureType != TextureImporterType.NormalMap)
                    {
                        texImporter.textureType = TextureImporterType.NormalMap;
                        texImporter.SaveAndReimport();
                    }
                    mat.EnableKeyword("_NORMALMAP");
                }
            }
        }

        // --- Префабы (извлекаются из общего FBX) ---
        private void ValidateWeaponBasePrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponBasePrefabPath);

            if (prefab == null && AutoFix)
            {
                AddLog($"Извлекаю {WeaponRootName} из общего FBX...");
                var weaponObject = ExtractSubAssetFromFBX(WeaponRootName);

                if (weaponObject != null)
                {
                    Undo.RegisterCreatedObjectUndo(weaponObject, "Create Weapon Prefab");

                    // Создаём слот для оптики
                    var slot = new GameObject("CZ600_Optic_slot");
                    Undo.RegisterCreatedObjectUndo(slot, "Create Slot");
                    slot.transform.SetParent(weaponObject.transform);
                    slot.transform.localPosition = new Vector3(0, 0.1f, 0.5f);

                    // Привязываем материал
                    var renderer = weaponObject.GetComponentInChildren<MeshRenderer>();
                    if (renderer != null)
                    {
                        var mat = AssetDatabase.LoadAssetAtPath<Material>(
                            "Assets/_Project/Art/Weapons/Materials/CZ600/CZ600_weapon_base_mat.mat");
                        if (mat != null) renderer.sharedMaterial = mat;
                    }

                    EnsureFolder(Path.GetDirectoryName(WeaponBasePrefabPath));
                    PrefabUtility.SaveAsPrefabAsset(weaponObject, WeaponBasePrefabPath);
                    Object.DestroyImmediate(weaponObject);
                    AddLog("Префаб оружия создан.");
                }
            }
            else if (prefab != null)
            {
                var slot = prefab.transform.Find("CZ600_Optic_slot");
                var renderer = prefab.GetComponentInChildren<MeshRenderer>();
                AddLog(renderer != null ? "Префаб оружия содержит меш." : "Предупреждение: В префабе нет MeshRenderer!", renderer == null);
                AddLog(slot != null ? "Слот для оптики присутствует." : "Предупреждение: Слот для оптики отсутствует.", slot == null);
            }
            else if (!AutoFix) AddLog("Префаб оружия отсутствует.", true);
        }

        private void ValidateOpticPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OpticPrefabPath);

            if (prefab == null && AutoFix)
            {
                AddLog($"Извлекаю {OpticRootName} из общего FBX...");
                var opticObject = ExtractSubAssetFromFBX(OpticRootName);

                if (opticObject != null)
                {
                    Undo.RegisterCreatedObjectUndo(opticObject, "Create Optic Prefab");

                    // Привязываем материал
                    var renderer = opticObject.GetComponentInChildren<MeshRenderer>();
                    if (renderer != null)
                    {
                        var mat = AssetDatabase.LoadAssetAtPath<Material>(
                            "Assets/_Project/Art/Weapons/Materials/CZ600/CZ600_optic_mat.mat");
                        if (mat != null) renderer.sharedMaterial = mat;
                    }

                    EnsureFolder(Path.GetDirectoryName(OpticPrefabPath));
                    PrefabUtility.SaveAsPrefabAsset(opticObject, OpticPrefabPath);
                    Object.DestroyImmediate(opticObject);
                    AddLog("Префаб прицела создан.");
                }
            }
            else if (prefab != null)
            {
                var renderer = prefab.GetComponentInChildren<MeshRenderer>();
                AddLog(renderer != null ? "Префаб прицела содержит меш." : "Предупреждение: В префабе прицела нет MeshRenderer!", renderer == null);
            }
            else if (!AutoFix) AddLog("Префаб прицела отсутствует.", true);
        }

        private void ValidateCombinedPrefab()
        {
            var combined = AssetDatabase.LoadAssetAtPath<GameObject>(CombinedPrefabPath);
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponBasePrefabPath);
            var opticPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OpticPrefabPath);

            if (combined == null && AutoFix && basePrefab && opticPrefab)
            {
                AddLog("Собираем комбинированный префаб...");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
                Undo.RegisterCreatedObjectUndo(instance, "Create Combined Prefab");

                var slot = instance.transform.Find("CZ600_Optic_slot");
                if (slot != null)
                {
                    var opticInstance = (GameObject)PrefabUtility.InstantiatePrefab(opticPrefab);
                    Undo.RegisterCreatedObjectUndo(opticInstance, "Attach Optic");
                    opticInstance.transform.SetParent(slot, false);
                    opticInstance.transform.localPosition = Vector3.zero;
                    opticInstance.transform.localRotation = Quaternion.identity;
                    AddLog("Прицел добавлен в слот.");
                }
                else AddLog("Слот для оптики не найден в базовом префабе!", true);

                EnsureFolder(Path.GetDirectoryName(CombinedPrefabPath));
                PrefabUtility.SaveAsPrefabAsset(instance, CombinedPrefabPath);
                Object.DestroyImmediate(instance);
                AddLog("Комбинированный префаб создан.");
            }
            else if (combined != null)
            {
                var hasOptic = combined.transform.Find("CZ600_Optic_slot")?.childCount > 0;
                AddLog(hasOptic ? "Комбинированный префаб содержит прицел." : "Предупреждение: Прицел не прикреплён!", !hasOptic);
            }
            else if (!AutoFix) AddLog("Комбинированный префаб отсутствует.", true);
        }

        private void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            if (!AutoFix)
            {
                AddLog($"Папка не найдена: {path}", true);
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string folderName = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
            AddLog($"Создана папка: {path}");
        }
    }
}