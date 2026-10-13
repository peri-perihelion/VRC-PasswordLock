using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace moth.passwordlock {
    public class Window : EditorWindow {
        private static VRCAvatarDescriptor[] avatars;
        private static VisualElement configEditor;
        private static LockConfig config;
        private static DropdownField avatarChoice;
        
        private static VRCAvatarDescriptor _activeAvatar;
        public static VRCAvatarDescriptor ActiveAvatar {
            get => _activeAvatar;
            set {
                // update the config each time the avatar changes
                if (value != ActiveAvatar) {
                    config = LockConfig.Load(value);
                    configEditor.Add(config.InspectorGUI(avatarChoice));

                    // remove all but the editor we just added
                    for (int i = configEditor.childCount - 2; i >= 0; i--) {
                        configEditor.RemoveAt(i);
                    }
                }
                _activeAvatar = value;
            }
        }

        [MenuItem("Tools/Password Lock/Open Control Panel")]
        public static void OpenPanel() {
            Window window = GetWindow<Window>();
            window.titleContent = new GUIContent("Password Lock");
        }

        public void CreateGUI() {
            // cover image
            rootVisualElement.Add(new Image(){
                image = EditorGUIUtility.Load($"{PasswordLocker.ROOT}/Editor/Resources/Cover.png") as Texture,
                
                style = {
                    backgroundColor = new Color(0.8f, 0.2f, 0.4f),

                    // image size and fitment
                    height = 66,
                    paddingBottom = 4,
                    paddingTop = 4,

                    // border colors to make it look a little more natural
                    borderBottomWidth = 2,
                    borderBottomColor = new Color(0.13f, 0.13f, 0.13f, 1),
                    borderLeftColor = new Color(0.13f, 0.13f, 0.13f, 1),
                    borderLeftWidth = 1
                }
            });

            // warning menu for when there are no active Avatars to lock
            HelpBox warnBox = new() {
                text = "No Avatars found! At least one avatar must be present (and enabled) to use this script.",
                messageType = HelpBoxMessageType.Error,
                style = { display = DisplayStyle.None }
            };
            rootVisualElement.Add(warnBox);

            VisualElement root = new() {
                // some margin to make the ui more readable
                style = {
                    marginLeft = 4,
                    marginRight = 4,
                    marginTop = 4,
                    marginBottom = 4
                }
            };
            rootVisualElement.Add(root);

            configEditor = new();
            avatarChoice = AvatarChoice();
            UpdateAvatarChoices(avatarChoice);

            // if the ui updates, but the active avatar goes unchanged, it will fail to draw an editor
            // this makes sure that if we have a config, we have an editor
            if (config != null && configEditor.childCount == 0) {
                configEditor.Add(config.InspectorGUI(avatarChoice));
            }

            rootVisualElement.RegisterCallback<MouseEnterEvent>(_ => {
                // update the avatar choice dropdown each time the mouse enters the window
                UpdateAvatarChoices(avatarChoice);
                
                // display a warning when no valid avatars are avaliable
                if (avatars.Length != 0) {
                    root.visible = true;
                    warnBox.style.display = DisplayStyle.None; 
                }
                else {
                    root.visible = false;
                    warnBox.style.display = DisplayStyle.Flex;
                }
            });

            root.Add(configEditor);

            Foldout tools = new() {
                text = "Tools",
                viewDataKey = "toolsDropdown"
            };
            {
                tools.contentContainer.Add(ClearConfigButton());
                tools.contentContainer.Add(RemoveDeprecatedButton());
                tools.contentContainer.Add(DebugToggle());
            }
            root.Add(tools);

            root.Add(PasswordButton());
            root.Add(UndoButton());

            root.Add(SourceCodeButton());
        }

        private DropdownField AvatarChoice() {
            DropdownField avatarChoice = new();
            avatarChoice.RegisterValueChangedCallback(evt => {
                ActiveAvatar = avatars[avatarChoice.index];
            });
            return avatarChoice;
        }
        /// <summary> Updates the displayed options in an AvatarChoice dropdown</summary>
        private void UpdateAvatarChoices(DropdownField avatarChoice) {
            List<VRCAvatarDescriptor> all = Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>().Reverse().ToList();
            // only show active avatars
            avatars = all.Where(avatar => avatar.gameObject.activeInHierarchy).ToArray();

            avatarChoice.choices.Clear();
            if (avatars.Length > 0) {
                if (ActiveAvatar == null) {
                    ActiveAvatar = avatars[0];
                }

                // recreate choices
                for (int i = 0; i < avatars.Length; i++) {
                    avatarChoice.choices.Add(avatars[i].name);
                    
                    // if the selected avatar still exists, set the index appropriately
                    if (avatars[i].name == ActiveAvatar.name) {
                        ActiveAvatar = avatars[i];
                        avatarChoice.index = i;
                    }
                }
            }
        }

        private Toggle DebugToggle() {
            Toggle debugToggle = new() {
                label = "Debug",
                tooltip = "Enable debugging tools.",
                value = LockConfig.debug
            };

            debugToggle.RegisterValueChangedCallback(_ => {
                LockConfig.debug = debugToggle.value;
            });

            return debugToggle;
        }
        
        private Button RemoveDeprecatedButton() {
            Button removeDeprecated = new() {
                text = "Remove GabSith Lock",
                tooltip = "Remove the deprecated GathSith password lock from this avatar."
            };
            
            removeDeprecated.RegisterCallback<ClickEvent>(evt => {
                if (!ActiveAvatar.RemoveDeprecated()) {
                    Debug.LogWarning("GathSith lock not detected.");
                }
            });

            return removeDeprecated;
        }
        private Button ClearConfigButton() {
            Button clearConfigButton = new() {
                text = "Clear All Configs",
                tooltip = "Clear all saved configurations for this tool."
            };
            clearConfigButton.RegisterCallback<ClickEvent>(_ => {
                bool result = EditorUtility.DisplayDialog(
                    "Are you sure?",
                    "This will forget all configuration data within this project, including avatar passwords! " +
                    "You'll still be able to undo password protection, but the passwords themself will be forgotten by this tool.",
                    "I know what im doing!", "Cancel");
                
                // primary use case of this button is me debugging the config system, but i could see
                // someone wanting to use it to clean up a particularly large config folder
                if (result) {
                    AssetDatabase.DeleteAsset($"{PasswordLocker.ROOT}{LockConfig.CONFIGS}");
                    // first index of configs is always going to be a slash, so we drop it when recreating the folder
                    AssetDatabase.CreateFolder($"{PasswordLocker.ROOT}", LockConfig.CONFIGS.Substring(1));

                    // recreate our currently active config
                    config = LockConfig.Load(ActiveAvatar);

                    Debug.Log("Cleared all user configuration files.");
                }
            });

            return clearConfigButton;
        }
        private Button PasswordButton() {
            Button button = new() {
                text = "Apply Password"
            };

            button.RegisterCallback<ClickEvent>(evt => {
                ActiveAvatar.Lock(config);
            });

            return button;
        }
        private Button UndoButton() {
            Button button = new() {
                text = "Undo Password Protection"
            };

            button.RegisterCallback<ClickEvent>(evt => {
                ActiveAvatar.Unlock();
            });

            return button;
        }
    
        private VisualElement SourceCodeButton() {
            Color linkColor = new(0.63f, 0.75f, 0.8f);
            Color hoverColor = new(0.83f, 0.95f, 1f);

            VisualElement root = new() {
                style = { flexDirection = FlexDirection.Row }
            };
            Label sourceLabel = new() {
                text = "Source Code",
                style = {
                    color = linkColor,
                    borderBottomColor = linkColor
                }
            };
            sourceLabel.RegisterCallback<MouseEnterEvent>(_ => {
                sourceLabel.style.borderBottomWidth = 1;
                sourceLabel.style.color = hoverColor;
            });
            sourceLabel.RegisterCallback<MouseLeaveEvent>(_ => {
                sourceLabel.style.borderBottomWidth = 0;
                sourceLabel.style.color = linkColor;
            });
            sourceLabel.RegisterCallback<ClickEvent>(_ => {
                Application.OpenURL("https://github.com/peri-perihelion/VRC-PasswordLock");
            });

            root.Add(new() {
                style = { flexGrow = 10 }
            });

            root.Add(sourceLabel);
            return root;
        }
    }
}
