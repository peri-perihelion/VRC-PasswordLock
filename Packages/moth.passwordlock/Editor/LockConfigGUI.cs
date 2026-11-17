using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.UIElements;

namespace moth.passwordlock {
    public static class LockConfigEditor {
        private static string lastValidPassword, lastValidLayerName;

        public static VisualElement InspectorGUI(this LockConfig config, VisualElement avatarChoice) {
            VisualElement root = new();
            VisualElement splitbox = SplitBox(new(1, 5), out var left, out var right);
            {
                left.Add(BoldLabel("Target Avatar"));
                left.Add(avatarChoice);
                left.Add(Password(config));

                right.Add(BoldLabel("Paramater Names"));
                right.Add(InputParameter(config));
                right.Add(LockParameter(config));
            }
            root.Add(splitbox);

            // TODO: Im sure I could clean this up...
            root.Add(new HelpBox() {
                text = "For optimal protection, parameter names should blend in with normal avatar parameters. Hover for more information.",
                tooltip = "Locks can be bypassed by forcing the lock parameter to be to false. " +
                            "Hiding the paramter makes this significantly harder.",
                messageType = HelpBoxMessageType.Info,
                style = { paddingBottom = 4 }
            });
            
            Foldout configuration = new() {
                text = "Configuration",
                viewDataKey = "configurationDropdown",
                value = false
            };
            {
                configuration.contentContainer.Add(LayerName(config));        
                configuration.contentContainer.Add(LockedAnimation(config));   
                configuration.contentContainer.Add(MenuIcon(config)); 
                configuration.contentContainer.Add(Decoy(config));
                configuration.contentContainer.Add(Obfuscation(config));
            }
            root.Add(configuration);

            return root;
        }
        
        private static TextField Password(LockConfig config) {
            lastValidPassword = config.password;

            TextField passwordField = new() {
                label = "Password",
                tooltip = "The password for unlocking the avatar.",
                value = config.password
            };
            // let the box expand as much as possible
            passwordField.ElementAt(0).style.minWidth = 67;

            passwordField.RegisterValueChangedCallback(_ => {
                string newPassword = passwordField.value;

                // check for invalidating features
                if (newPassword.Length > 0) {
                    // menus only have room for 8 controls, and only int input is supported
                    if (newPassword.Contains('0') || newPassword.Contains('9') || !int.TryParse(newPassword, out int pass)) {
                        config.password = lastValidPassword;
                        passwordField.value = lastValidPassword;
                        return;
                    }

                    // repeat digits would just be skipped, so we prevent them
                    int previousDigit = 0;
                    foreach (int digit in pass.GetDigits()) {
                        if (previousDigit == digit) {
                            config.password = lastValidPassword;
                            passwordField.value = lastValidPassword;
                            return;
                        }
                        previousDigit = digit;
                    }
                }

                config.password = passwordField.value;
                lastValidPassword = passwordField.value;
            });
            return passwordField;
        }
        private static VisualElement InputParameter(LockConfig config) {
            TextField inputParameterField = new() {
                label = "Input",
                tooltip = "The paramter that stores password input.",
                value = config.inputParameter,
            };
            // let the box expand as much as possible
            inputParameterField.ElementAt(0).style.minWidth = 40;

            inputParameterField.RegisterValueChangedCallback(_ => {
                config.inputParameter = inputParameterField.value;
            });

            var root = DefaultButton(inputParameterField, out var button);
            button.RegisterCallback<ClickEvent>(_ => {
                inputParameterField.value = "VRCSyncInt";
            });

            return root;
        }
        private static VisualElement LockParameter(LockConfig config) {
            TextField lockParameterField = new() {
                label = "Lock",
                tooltip = "The paramter that stores avatar lock state.",
                value = config.lockParameter,
            };
            // let the box expand as much as possible
            lockParameterField.ElementAt(0).style.minWidth = 40;

            lockParameterField.RegisterValueChangedCallback(evt => {
                config.lockParameter = lockParameterField.value;
            });

            var root = DefaultButton(lockParameterField, out var button);
            button.RegisterCallback<ClickEvent>(_ => {
                lockParameterField.value = "VRCSyncBool";
            });

            return root;
        }
        private static VisualElement LayerName(LockConfig config) {
            lastValidLayerName = config.layerName;

            TextField layerNameField = new() {
                label = "Layer Name",
                tooltip = "The name that the locking layer will have.",
                value = config.layerName
            };

            layerNameField.RegisterValueChangedCallback(_ => {
                string newName = layerNameField.value;

                if (newName.Contains('.')) {
                    config.layerName = lastValidLayerName;
                    layerNameField.value = config.layerName;
                    return;
                }

                config.layerName = newName;
                lastValidLayerName = newName;
            });

            var root = DefaultButton(layerNameField, out var button);
            button.RegisterCallback<ClickEvent>(_ => {
                layerNameField.value = "Password Lock";
            });
            return root;
        }
        
        private static VisualElement LockedAnimation(LockConfig config) {
            ObjectField lockedAnimationField = new() {
                label = "Locked Animation",
                tooltip = "The animation the avatar will use when locked",
                objectType = typeof(AnimationClip),
                value = config.lockAnimation
            };
            lockedAnimationField.RegisterValueChangedCallback(_ => {
                config.lockAnimation = lockedAnimationField.value as AnimationClip;
            });

            var root = DefaultButton(lockedAnimationField, out var button);
            button.RegisterCallback<ClickEvent>(_ => {
                lockedAnimationField.value = null;
            });

            return root;
        }
        private static VisualElement MenuIcon(LockConfig config) {
            ObjectField menuIconField = new() {
                label = "Menu Icon",
                tooltip = "The icon for the unlock menu.",
                objectType = typeof(Texture2D),
                value = config.menuIcon,
                style = { flexGrow = 10 }
            };
            menuIconField.RegisterValueChangedCallback(_ => {
                config.menuIcon = menuIconField.value as Texture2D;
            });

            var root = DefaultButton(menuIconField, out var button);
            button.RegisterCallback<ClickEvent>(_ => {
                menuIconField.value = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PasswordLocker.ROOT}{PasswordLocker.DEFAULTS}/Icon.png");
            });

            return root;
        }

        private static Toggle Decoy(LockConfig config) {
            Toggle decoyToggle = new() {
                label = "Decoy Parameter",
                tooltip = "Adds a decoy \"_locked\" parameter.",
                value = config.useDecoy
            };

            decoyToggle.RegisterValueChangedCallback(_ => {
                // check if theres enough room to add the decoy variable
                if (decoyToggle.value) {
                    var parameters = Window.ActiveAvatar.expressionParameters;
                    if (parameters != null) {
                        // bool variables cost 1 bit, and the max is 256
                        if (parameters.CalcTotalCost() > 255) {
                            Debug.LogWarning("Cannot enable decoy variable, parameters is full.");
                            decoyToggle.value = false;
                            return;
                        }
                    }
                }

                config.useDecoy = decoyToggle.value;
            });
            return decoyToggle;
        }
    
        private static VisualElement Obfuscation(LockConfig config) {
            VisualElement root = new();
            DropdownField obfuscationChoice = new() {
                label = "Obfuscation Mode",
                tooltip = "Adjust the obfuscation protection mode.",
                choices = {
                    "None", "Lock Only", "All"
                },
                index = config.obfuscationMode
            };
            root.Add(obfuscationChoice);

            HelpBox description = new() {
                text = DescribeObfuscation(obfuscationChoice.index),
                messageType = HelpBoxMessageType.Info
            };
            root.Add(description);

            obfuscationChoice.RegisterValueChangedCallback(_ => {
                config.obfuscationMode = obfuscationChoice.index;
                description.text = DescribeObfuscation(obfuscationChoice.index);
            });
            return root;
        }
        private static string DescribeObfuscation(int index) {
            return index switch {
                0 => "No obfuscation will be applied.",
                1 => "The lock will be obfuscated to hide its password from would-be avatar rippers. However, removing protection is trivial.",
                2 => "All FX layers will be obfuscated. A ripped avatar would need to have its animator recreated from scratch to remove the lock.",
                _ => "Unknown obfuscation mode."
            };
        }
        
        /// <summary> Shorthand for creating a label with a bold font style. </summary>
        private static Label BoldLabel(string text) {
            return new() {
                text = text,
                style = { unityFontStyleAndWeight = FontStyle.Bold }
            };
        }
        /// <summary> Creates an element with a left and right split. </summary>
        private static VisualElement SplitBox(Vector2Int ratio, out VisualElement left, out VisualElement right) {
            VisualElement splitBox = new() {
                style = { flexDirection = FlexDirection.Row }  
            };

            left = new() {
                style = { flexGrow = ratio.x }
            };
            splitBox.Add(left);

            right = new() {
                style = { flexGrow = ratio.y }
            };
            splitBox.Add(right); 

            return splitBox;
        }
        /// <summary> Adds a default button to a field. </summary>
        private static VisualElement DefaultButton(VisualElement field, out Button button) {
            VisualElement root = new() {
                style = {flexDirection = FlexDirection.Row}
            };

            button = new() {
                text = "X",
                tooltip = "Reset to default value.",
                style = {marginLeft = -1}
            };
            field.style.flexGrow = 10;

            root.Add(field);
            root.Add(button);
            return root;
        }
    }
}