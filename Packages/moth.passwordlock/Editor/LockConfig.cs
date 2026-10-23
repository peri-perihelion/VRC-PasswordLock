using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace moth.passwordlock {
    public class LockConfig: ScriptableObject {
        public const string CONFIGS = "/Configs";

        public Texture2D menuIcon;
        public AnimationClip lockAnimation;

        public string password, inputParameter, lockParameter, layerName;

        public int obfuscationMode;

        public bool useDecoy;
        public static bool debug;

        public LockConfig() {
            password = "1234";
            inputParameter = "VRCSyncInt";
            lockParameter = "VRCSyncBool";
            layerName = "Password Lock";
            obfuscationMode = 0;
        }
        /// <summary> Loads or creates a config for the given avatar </summary>
        public static LockConfig Load(VRCAvatarDescriptor avatar) {
            string folder = $"{PasswordLocker.ROOT}{CONFIGS}";
            string path = $"{folder}/{avatar.name}.asset";

            // create the configs folder if it doesnt exist
            if (!AssetDatabase.IsValidFolder(folder)) {
                // first character of configs is always a slash
                AssetDatabase.CreateFolder(PasswordLocker.ROOT, CONFIGS.Substring(1));
            }

            var config = AssetDatabase.LoadAssetAtPath<LockConfig>(path);
            if (config == null) {
                if (avatar.IsLocked()) {
                    // avatar name may have changed
                    string parameterPath = AssetDatabase.GetAssetPath(avatar.expressionParameters);
                    parameterPath = parameterPath.Substring(PasswordLocker.ROOT.Length);
                    
                    // TODO: Locate config for locked avatars whos name has changed
                    // we can do this by using the directory, but i just have to get around to it.
                    Debug.Log(parameterPath);
                }

                // create a new one
                config = CreateInstance<LockConfig>();
                // asset database cant be used in a scriptable object constructor, so this is the second best place for this
                config.menuIcon = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PasswordLocker.ROOT}{PasswordLocker.DEFAULTS}/Icon.png");
                AssetDatabase.CreateAsset(config, path);
            }

            return config;
        }
    }
}