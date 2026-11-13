using System;
using LiveStock.Economy;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LiveStock.Items.Tests
{
    public sealed class ItemProjectilePhysicsTests
    {
        // An asmdef cannot reference predefined Assembly-CSharp. Exercise its public
        // manager API by runtime type; observe real Rigidbody motion, not source text.
        private static readonly Type ManagerType = Type.GetType("LiveStock.Items.ItemUseManager, Assembly-CSharp", true);
        private static readonly Type ProjectileType = Type.GetType("LiveStock.Items.ItemProjectile, Assembly-CSharp", true);
        private static readonly string[] ItemIds = { "capture-ball", "bow", "sniper", "bomb" };

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.SetActive(false);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static void Initialize(MonoBehaviour behaviour)
        {
            // Preview scenes do not dispatch non-ExecuteAlways lifecycle methods.
            // SendMessage would hit Unity's play-mode assertion in this context.
            behaviour.GetType().GetMethod("Awake",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(behaviour, null);
        }

        private static void Reference(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void WarmedProjectileMovesAndReusedProjectileGetsItsNewVelocity()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var physics = scene.GetPhysicsScene();
            Assert.That(physics.IsValid(), Is.True);
            Assert.That(physics, Is.Not.EqualTo(Physics.defaultPhysicsScene), "Never simulate the user's scene.");
            var root = new GameObject("Isolated item physics fixture");
            SceneManager.MoveGameObjectToScene(root, scene);
            MonoBehaviour manager = null;
            try
            {
                var moneyObject = Child(root, "Wallet");
                var money = moneyObject.AddComponent<MoneyManager>();
                moneyObject.SetActive(true);
                // Non-ExecuteAlways components do not receive Awake in EditMode.
                Initialize(money);
                Assert.That(money.RegisterPlayer("A"), Is.True);
                Assert.That(money.RegisterPlayer("B"), Is.True);
                money.AddMoney("A", 10000);
                money.AddMoney("B", 10000);

                var shopObject = Child(root, "Inventory");
                var shop = shopObject.AddComponent<ShopManager>();
                Reference(shop, "moneyManager", money);
                shopObject.SetActive(true);
                if (shop.ProductCount == 0) Initialize(shop);
                Assert.That(shop.RegisterPlayer("A"), Is.True);
                Assert.That(shop.RegisterPlayer("B"), Is.True);
                Assert.That(shop.Purchase("A", "bow", 1).Status, Is.EqualTo(ShopState.PurchaseStatus.Success));
                Assert.That(shop.Purchase("B", "bow", 1).Status, Is.EqualTo(ShopState.PurchaseStatus.Success));

                var managerObject = Child(root, "Item use");
                manager = (MonoBehaviour)managerObject.AddComponent(ManagerType);
                var settings = new SerializedObject(manager);
                settings.FindProperty("shopManager").objectReferenceValue = shop;
                settings.FindProperty("useCooldown").floatValue = 0;
                var bindings = settings.FindProperty("projectiles");
                for (int i = 0; i < ItemIds.Length; i++)
                {
                    var prototype = Child(root, ItemIds[i] + " prototype");
                    var projectile = prototype.AddComponent(ProjectileType);
                    var profile = new SerializedObject(projectile);
                    profile.FindProperty("speed").floatValue = 7;
                    profile.FindProperty("upwardSpeed").floatValue = 0;
                    profile.FindProperty("useGravity").boolValue = false;
                    profile.ApplyModifiedPropertiesWithoutUndo();
                    var binding = bindings.GetArrayElementAtIndex(i);
                    binding.FindPropertyRelative("itemId").stringValue = ItemIds[i];
                    binding.FindPropertyRelative("prefab").objectReferenceValue = projectile;
                }
                settings.ApplyModifiedPropertiesWithoutUndo();
                managerObject.SetActive(true);
                if (!(bool)ManagerType.GetMethod("IsConfiguredFor").Invoke(manager, new object[] { shop }))
                    Initialize(manager);
                var register = ManagerType.GetMethod("RegisterPlayer");
                Assert.That(register.Invoke(manager, new object[] { "A" }), Is.True);
                Assert.That(register.Invoke(manager, new object[] { "B" }), Is.True);
                var use = ManagerType.GetMethod("Use");

                var first = (ItemUseState.UseResult)use.Invoke(manager,
                    new object[] { "A", "bow", 1, new Ray(Vector3.zero, Vector3.forward), 0, 0, true });
                Assert.That(first.ShouldLaunch, Is.True);
                var bodies = manager.GetComponentsInChildren<Rigidbody>();
                Assert.That(bodies, Has.Length.EqualTo(1));
                var body = bodies[0];
                Assert.That(body.linearVelocity.z, Is.EqualTo(7).Within(0.001f));
                var start = body.position;
                physics.Simulate(0.02f);
                Assert.That(body.position.z, Is.GreaterThan(start.z + 0.1f));
                Assert.That(shop.TryGetQuantity("A", "bow", out int quantityA), Is.True);
                Assert.That(quantityA, Is.Zero);

                // A disconnected shot returns to the pool; B must get real motion,
                // not A's velocity or the zero velocity restored during activation.
                Assert.That(ManagerType.GetMethod("RemovePlayer").Invoke(manager, new object[] { "A" }), Is.True);
                Assert.That(body.gameObject.activeSelf, Is.False);
                var second = (ItemUseState.UseResult)use.Invoke(manager,
                    new object[] { "B", "bow", 1, new Ray(Vector3.zero, Vector3.right), 0, 0, true });
                Assert.That(second.ShouldLaunch, Is.True);
                bodies = manager.GetComponentsInChildren<Rigidbody>();
                Assert.That(bodies, Has.Length.EqualTo(1));
                Assert.That(bodies[0], Is.SameAs(body));
                Assert.That(body.linearVelocity.x, Is.EqualTo(7).Within(0.001f));
                Assert.That(body.linearVelocity.z, Is.Zero.Within(0.001f));
                start = body.position;
                physics.Simulate(0.02f);
                Assert.That(body.position.x, Is.GreaterThan(start.x + 0.1f));
                Assert.That(shop.TryGetQuantity("B", "bow", out int quantityB), Is.True);
                Assert.That(quantityB, Is.Zero);
            }
            finally
            {
                if (manager != null)
                {
                    ManagerType.GetMethod("Clear").Invoke(manager, null);
                    // Runtime pools use deferred Destroy; remove inactive children
                    // first so EditMode cleanup never invokes it on live objects.
                    for (int i = manager.transform.childCount - 1; i >= 0; i--)
                        Object.DestroyImmediate(manager.transform.GetChild(i).gameObject);
                }
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
