using System;
using System.Collections.Generic;
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
    }

    private static void Test_Game_TheKey()
    {
        True(typeof(ZInput).GetMethod("GetKeyDown", new[] { typeof(KeyCode), typeof(bool) }) != null, "ZInput.GetKeyDown(KeyCode, bool)");
        True(typeof(ZInput).GetMethod("GetKey", new[] { typeof(KeyCode), typeof(bool) }) != null, "ZInput.GetKey(KeyCode, bool)");
    }

    private static void Test_Game_TheMessage()
    {
        Param(Method(typeof(MessageHud), "ShowMessage"), "log", typeof(bool));
    }

    private static void Test_Game_TheHint()
    {
        Field(typeof(KeyHints), "m_combatHints", typeof(GameObject));
    }

    private static void Test_Game_TheRecord()
    {
        var update = Method(typeof(VisEquipment), "CustomUpdate");
        Param(update, "deltaTime", typeof(float));
        Param(update, "time", typeof(float));
        Field(typeof(VisEquipment), "m_isPlayer", typeof(bool));
        Field(typeof(VisEquipment), "m_isArmorStand", typeof(bool));
        Eq(typeof(ZDO), typeof(ZNetView).GetMethod("GetZDO", Type.EmptyTypes)?.ReturnType, "ZNetView.GetZDO()");
        Eq(typeof(int), typeof(ZDO).GetMethod("GetInt", new[] { typeof(int), typeof(int) })?.ReturnType, "ZDO.GetInt(int, int)");
        True(typeof(ZDO).GetMethod("Set", new[] { typeof(int), typeof(int), typeof(bool) }) != null, "ZDO.Set(int, int, bool)");
        // The look codes and the record key use the game's own string hash, the one its record keys are made with.
        Eq(ZDOVars.s_rightItem, "RightItem".GetStableHashCode(), "GetStableHashCode makes the game's record keys");
    }

    private static void Test_Game_TheStaffNote()
    {
        Field(typeof(ItemDrop.ItemData), "m_customData", typeof(Dictionary<string, string>));
        Field(typeof(ItemDrop.ItemData), "m_dropPrefab", typeof(GameObject));
        var load = typeof(ItemDrop).GetMethod("LoadFromZDO", new[] { typeof(ItemDrop.ItemData), typeof(ZDO), typeof(int) });
        True(load != null && load.IsStatic, "static ItemDrop.LoadFromZDO(ItemData, ZDO, int)");
        Eq(typeof(uint), typeof(ZDO).GetProperty("DataRevision")?.PropertyType, "ZDO.DataRevision");
    }

    private static void Test_Game_TheArmorStand()
    {
        Field(typeof(ArmorStand), "m_slots", typeof(List<ArmorStand.ArmorStandSlot>));
        Field(typeof(ArmorStand.ArmorStandSlot), "m_slot", typeof(VisSlot));
        // The model slots, in Dresser's model order: right / left hand, right / left back.
        Eq("HandRight,HandLeft,BackRight,BackLeft",
            string.Join(",", new[] { VisSlot.HandRight, VisSlot.HandLeft, VisSlot.BackRight, VisSlot.BackLeft }), "VisSlot names");
    }

    private static void Test_Game_TheGroundAndTheTooltip()
    {
        var tooltip = typeof(ItemDrop.ItemData).GetMethod("GetTooltip",
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) });
        True(tooltip != null && tooltip.IsStatic && tooltip.ReturnType == typeof(string), "static string ItemData.GetTooltip(ItemData, int, bool, float, int, bool)");
    }
}
