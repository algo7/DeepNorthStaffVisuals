using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// The game members the patches rely on, checked against the game DLL: a game update that renames or retypes one fails
/// here instead of silently in game.
/// </summary>
internal static partial class Tests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static void Field(Type type, string name, Type fieldType)
    {
        var field = type.GetField(name, Instance);
        True(field != null, $"{type.Name}.{name} exists");
        Eq(fieldType, field.FieldType, $"{type.Name}.{name} type");
    }

    private static MethodInfo Method(Type type, string name)
    {
        var methods = type.GetMethods(Instance).Where(m => m.Name == name && m.DeclaringType == type).ToArray();
        Eq(1, methods.Length, $"{type.Name}.{name}: one method (Harmony patches it by name)");
        return methods[0];
    }

    private static void Param(MethodInfo method, string name, Type type)
    {
        var p = method.GetParameters().FirstOrDefault(x => x.Name == name);
        True(p != null, $"{method.DeclaringType.Name}.{method.Name} has '{name}'");
        Eq(type, p.ParameterType, $"{method.DeclaringType.Name}.{method.Name} '{name}' type");
    }

    private static void Test_Game_TheItem()
    {
        Field(typeof(ItemDrop), "m_itemData", typeof(ItemDrop.ItemData));
        Field(typeof(ItemDrop.ItemData), "m_shared", typeof(ItemDrop.ItemData.SharedData));
        Field(typeof(ItemDrop.ItemData.SharedData), "m_itemType", typeof(ItemDrop.ItemData.ItemType));
        Field(typeof(ItemDrop.ItemData.SharedData), "m_skillType", typeof(Skills.SkillType));
        Eq(typeof(GameObject), typeof(ObjectDB).GetMethod("GetItemPrefab", new[] { typeof(int) })?.ReturnType, "ObjectDB.GetItemPrefab(int)");
        Eq(typeof(GameObject), typeof(ObjectDB).GetMethod("GetItemPrefab", new[] { typeof(string) })?.ReturnType, "ObjectDB.GetItemPrefab(string)");
    }

    private static void Test_Game_TheEquipment()
    {
        var attach = Method(typeof(VisEquipment), "AttachItem");
        Eq(typeof(GameObject), attach.ReturnType, "VisEquipment.AttachItem returns the model");
        Param(attach, "itemHash", typeof(int));
        foreach (var name in new[] { "m_rightItemInstance", "m_leftItemInstance", "m_rightBackItemInstance", "m_leftBackItemInstance" })
            Field(typeof(VisEquipment), name, typeof(GameObject));
        foreach (var name in new[] { "m_currentRightItemHash", "m_currentLeftItemHash", "m_currentRightBackItemHash", "m_currentLeftBackItemHash" })
            Field(typeof(VisEquipment), name, typeof(int));
    }

    private static void Test_Game_TheKey()
    {
        var takeInput = Method(typeof(Player), "TakeInput");
        Eq(typeof(bool), takeInput.ReturnType, "Player.TakeInput returns bool");
        Eq(0, takeInput.GetParameters().Length, "Player.TakeInput()");
        True(typeof(ZInput).GetMethod("GetKeyDown", new[] { typeof(KeyCode), typeof(bool) }) != null, "ZInput.GetKeyDown(KeyCode, bool)");
        True(typeof(ZInput).GetMethod("GetKey", new[] { typeof(KeyCode), typeof(bool) }) != null, "ZInput.GetKey(KeyCode, bool)");
    }

    private static void Test_Game_TheMessage()
    {
        Param(Method(typeof(MessageHud), "ShowMessage"), "log", typeof(bool));
    }

    private static void Test_Game_TheHint()
    {
        True(Method(typeof(KeyHints), "UpdateHints").GetParameters().Length == 0, "KeyHints.UpdateHints()");
        Field(typeof(KeyHints), "m_combatHints", typeof(GameObject));
    }
}
