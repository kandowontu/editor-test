using Godot;
using System;
using System.Collections.Generic;

namespace FamidashEditor;

/// <summary>
/// Undo/Redo system for map editing operations
/// </summary>
public partial class UndoRedoManager : GodotObject
{
    private readonly Stack<IUndoAction> _undoStack = new();
    private readonly Stack<IUndoAction> _redoStack = new();
    private readonly MapData _mapData;
    
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    
    public UndoRedoManager(MapData mapData)
    {
        _mapData = mapData;
    }
    
    public void RecordAction(IUndoAction action)
    {
        _undoStack.Push(action);
        _redoStack.Clear(); // Clear redo stack when new action is recorded
    }
    
    public void Undo()
    {
        if (!CanUndo) return;
        
        var action = _undoStack.Pop();
        action.Undo(_mapData);
        _redoStack.Push(action);
    }
    
    public void Redo()
    {
        if (!CanRedo) return;
        
        var action = _redoStack.Pop();
        action.Redo(_mapData);
        _undoStack.Push(action);
    }
    
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}

public interface IUndoAction
{
    void Undo(MapData mapData);
    void Redo(MapData mapData);
}

/// <summary>
/// Records a single tile/sprite change
/// </summary>
public class SetCellAction : IUndoAction
{
    private readonly int _x, _y;
    private readonly int _oldTileValue, _newTileValue;
    private readonly int _oldSpriteValue, _newSpriteValue;
    private readonly bool _affectsTiles, _affectsSprites;
    
    public SetCellAction(int x, int y, int oldTile, int newTile, int oldSprite, int newSprite, 
                        bool affectsTiles, bool affectsSprites)
    {
        _x = x;
        _y = y;
        _oldTileValue = oldTile;
        _newTileValue = newTile;
        _oldSpriteValue = oldSprite;
        _newSpriteValue = newSprite;
        _affectsTiles = affectsTiles;
        _affectsSprites = affectsSprites;
    }
    
    public void Undo(MapData mapData)
    {
        if (_affectsTiles) mapData.SetTile(_x, _y, _oldTileValue);
        if (_affectsSprites) mapData.SetSprite(_x, _y, _oldSpriteValue);
    }
    
    public void Redo(MapData mapData)
    {
        if (_affectsTiles) mapData.SetTile(_x, _y, _newTileValue);
        if (_affectsSprites) mapData.SetSprite(_x, _y, _newSpriteValue);
    }
}

/// <summary>
/// Batches multiple cell changes into a single undoable action
/// </summary>
public class BatchSetAction : IUndoAction
{
    private readonly List<SetCellAction> _actions = new();
    
    public void AddAction(SetCellAction action)
    {
        _actions.Add(action);
    }
    
    public void Undo(MapData mapData)
    {
        // Undo in reverse order
        for (int i = _actions.Count - 1; i >= 0; i--)
        {
            _actions[i].Undo(mapData);
        }
    }
    
    public void Redo(MapData mapData)
    {
        foreach (var action in _actions)
        {
            action.Redo(mapData);
        }
    }
}
