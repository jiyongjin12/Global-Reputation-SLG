using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class PlacementSystem : MonoBehaviour
{
    [SerializeField]
    private InputManager inputManager;
    [SerializeField]
    private Grid grid;

    [SerializeField]
    private ObjectsDatabaseSO database;

    [SerializeField]
    private GameObject gridVisualization;

    //[SerializeField]
    //private AudioClip correctPlacementClip, wrongPlacementClip;
    //[SerializeField]
    //private AudioSource source;

    private GridData floorData, furnitureData;

    [SerializeField]
    private PreviewSystem preview;

    private Vector3Int lastDetectedPosition = Vector3Int.zero;

    [SerializeField]
    private ObjectPlacer objectPlacer;

    IBuildingState buildingState;

    //[SerializeField]
    //private SoundFeedback soundFeedback;

    private void Start()
    {
        gridVisualization.SetActive(false);
        floorData = new();
        furnitureData = new();
    }

    public void StartPlacement(int ID)
    {
        StopPlacement();
        gridVisualization.SetActive(true);
        buildingState = new PlacementState(ID, grid, preview, database, floorData, furnitureData, objectPlacer);
        inputManager.OnClicked += PlaceStructure;
        inputManager.OnExit += StopPlacement;
    }

    public void StartRemoving()
    {
        StopPlacement();
        gridVisualization.SetActive(true);
        buildingState = new RemovingState(grid, preview, floorData, furnitureData, objectPlacer);
        inputManager.OnClicked += PlaceStructure;
        inputManager.OnExit += StopPlacement;
    }

    private void PlaceStructure()
    {
        if (inputManager.IsPointerOverUI())
        {
            return;
        }
        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);

        buildingState.OnAction(gridPosition);

    }

    //private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex)
    //{
    //    GridData selectedData = database.objectsData[selectedObjectIndex].ID == 0 ? 
    //        floorData : 
    //        furnitureData;

    //    return selectedData.CanPlaceObejctAt(gridPosition, database.objectsData[selectedObjectIndex].Size);
    //}

    private void StopPlacement()
    {
        //soundFeedback.PlaySound(SoundType.Click);
        if (buildingState == null)
            return;
        gridVisualization.SetActive(false);
        buildingState.EndState();
        inputManager.OnClicked -= PlaceStructure;
        inputManager.OnExit -= StopPlacement;
        lastDetectedPosition = Vector3Int.zero;
        buildingState = null;
    }

    private void Update()
    {
        if (buildingState == null)
            return;
        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);
        if (lastDetectedPosition != gridPosition)
        {
            buildingState.UpdateState(gridPosition);
            lastDetectedPosition = gridPosition;
        }

    }


    /*
    [SerializeField]
    private GameObject mouseIndicator;
    [SerializeField]
    private InputManager inputManager;
    [SerializeField]
    private Grid grid;

    [SerializeField]
    private ObjectsDatabaseSO dataBase;
    private int selectedObjectIndex;

    [SerializeField]
    private GameObject gridVisualization;

    private GridData floorData, furnitureData;

    private List<GameObject> placedGameObjects = new();

    [SerializeField]
    private PreviewSystem preview;

    private Vector3Int lastDetectedPosition = Vector3Int.zero;

    private void Start()
    {
        StopPlacement();
        floorData = new();
        furnitureData = new();
    }

    public void StartPlacement(int ID)
    {
        StopPlacement();
        selectedObjectIndex = dataBase.objectsData.FindIndex(data => data.ID == ID);
        if (selectedObjectIndex < 0)
        {
            Debug.Log($"아이디 없으 {ID}");
            return;
        }
        gridVisualization.SetActive(true);
        preview.StartShowingPlacementPreview(dataBase.objectsData[selectedObjectIndex].Prefab, dataBase.objectsData[selectedObjectIndex].Size);
        inputManager.OnClicked += PlaceStrucure;
        inputManager.OnExit += StopPlacement;
    }

    private void PlaceStrucure()
    {
        if (inputManager.IsPointerOverUI())
        {
            return;
        }
        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);

        bool placementValidity =  CheckPlacementValidity(gridPosition, selectedObjectIndex);
        if (placementValidity == false)
            return;

        GameObject newObject = Instantiate(dataBase.objectsData[selectedObjectIndex].Prefab);
        newObject.transform.position = grid.CellToLocal(gridPosition);
        placedGameObjects.Add(newObject);
        GridData selectedData = dataBase.objectsData[selectedObjectIndex].ID == 0 ? floorData : furnitureData;

        selectedData.AddObjectAt(gridPosition, dataBase.objectsData[selectedObjectIndex].Size, dataBase.objectsData[selectedObjectIndex].ID, placedGameObjects.Count - 1);

        preview.UpdatePosition(grid.CellToWorld(gridPosition), false);
    }   

    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex)
    {
        GridData selectedData = dataBase.objectsData[selectedObjectIndex].ID == 0 ? floorData : furnitureData;

        return selectedData.CanPlaceObejctAt(gridPosition, dataBase.objectsData[selectedObjectIndex].Size);
    }

    private void StopPlacement()
    {
        selectedObjectIndex = -1;
        gridVisualization.SetActive(false);
        preview.StopShowingPreview();
        inputManager.OnClicked -= PlaceStrucure;
        inputManager.OnExit -= StopPlacement;
        lastDetectedPosition = Vector3Int.zero;
    }

    private void Update()
    {
        if (selectedObjectIndex < 0)
            return;
        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);

        if (lastDetectedPosition != gridPosition)
        {
            bool placementValidity = CheckPlacementValidity(gridPosition, selectedObjectIndex);

            mouseIndicator.transform.position = mousePosition;
            preview.UpdatePosition(grid.CellToWorld(gridPosition), placementValidity);
            lastDetectedPosition = gridPosition;
        }
    }
    */
}
