﻿//-----------------------------------------------------------------------
// <copyright file="DynamicTerrainChunk.cs" company="Quill18 Productions">
//     Copyright (c) Quill18 Productions. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DynamicTerrainChunk : MonoBehaviour 
{
    /// <summary>
    /// The image texture used to populate the height map.
    /// Colour data is discarded: Only greyscale matters.
    /// </summary>
    public Texture2D HeightMapTexture;

    /// <summary>
    /// Unused at this time.
    /// </summary>
    public Texture2D StructureMapTexture;

    /// <summary>
    /// The array of splat data used to paint the terrain
    /// </summary>
    public SplatData[] Splats;

    public StructureColor[] StructureColors;

    /// <summary>
    /// We're going to dampen the heightmap by this amount. Larger values means a flatter/smoother map.
    /// </summary>
    float heightMapTextureScaling = 2f;

    // The rotation that represents the point in the center of this terrain chunk
    // Reminder: You can easily convert this to a SphericalCoord (i.e. Lat/Long)
    public Quaternion ChunkRotation;

    // Degrees per chunk is used to determine the Rotation (or SphericalCoord)
    // of any point on our terrain. This can be used to tell the player what
    // his/her lat/long is, but ALSO we can be UV information from this
    public float DegreesPerChunk;

    public float WorldUnitsPerChunk;

    Terrain terrain;
    TerrainData terrainData;

    public void BuildTerrain(  )
    {
        // Create Terrain and TerrainCollider components and add them to the GameObject
        terrain = gameObject.AddComponent<Terrain>();
        TerrainCollider terrainCollider = gameObject.AddComponent<TerrainCollider>();

        // Everything about the terrain is actually stored in a TerrainData
        terrainData = new TerrainData();

        // Create the landscape
        BuildTerrainData(terrainData);

        // Define the "Splats" (terrain textures)
        BuildSplats(terrainData);

        // Apply the splats based on cliffiness
        PaintCliffs(terrainData);

        // Apply the data to the terrain and collider
        terrain.terrainData = terrainData;
        terrainCollider.terrainData = terrainData;


        // NOTE: If you make any changes to the terrain data after
        //       this, you should call terrain.Flush()


        // Now make the structures

        BuildStructures();

    }

    void BuildTerrainData(TerrainData terrainData)
    {
        // Define the size of the arrays that Unity's terrain will
        // use internally to represent the terrain. Bigger numbers
        // mean more fine details.


        // "Heightmap Resolution": "Pixel resolution of the terrain’s heightmap (should be a power of two plus one, eg, 513 = 512 + 1)."
        // AFAIK, this defines the size of the 2-dimensional array that holds the information about the terrain (i.e. terrainData.GetHeights())
        // Larger numbers lead to finer terrain details (if populated by a suitable source image heightmap).
        // As for actual physical size of the terrain (in Unity world space), this is defined as:
        //              terrainData.Size = terrainData.heightmapScale * terrainData.heightmapResolution
        terrainData.heightmapResolution = 128 + 1;

        // "Base Texture Resolution": "Resolution of the composite texture used on the terrain when viewed from a distance greater than the Basemap Distance"
        // AFAIK, this doesn't affect the terrain mesh -- only how the terrain texture (i.e. Splats) are rendered
        terrainData.baseMapResolution = 512 + 1;

        // "Detail Resolution" and "Detail Resolution Per Patch"
        // (used for Details -- i.e. grass/flowers/etc...)
        terrainData.SetDetailResolution( 1024, 32 );

        // Set the Unity worldspace size of the terrain AFTER you set the resolution.
        // This effectively just sets terrainData.heightmapScale for you, depending on the value of terrainData.heightmapResolution
        terrainData.size = new Vector3( WorldUnitsPerChunk, 512 , WorldUnitsPerChunk );

        // Get the 2-dimensional array of floats that defines the actual height data for the terrain.
        // Each float has a value from 0..1, where a value of 1 means the maximum height of the terrain as defined by terrainData.size.y
        //
        // AFAIK, terrainData.heightmapWidth and terrainData.heightmapHeight will always be equal to terrainData.heightmapResolution
        //
        float[,] heights = terrainData.GetHeights(0, 0, terrainData.heightmapWidth, terrainData.heightmapHeight);

        float halfDegreesPerChunk = DegreesPerChunk / 2f;

        // Loop through each point in the terrainData heightmap.
        for (int x = 0; x < terrainData.heightmapWidth; x++)
        {
            for (int y = 0; y < terrainData.heightmapHeight; y++)
            {
                // Normalize x and y to a value from 0..1
                // NOTE: We are INVERTING the x and y because internally Unity does this
                float xPos = (float)x / (float)terrainData.heightmapWidth;
                float yPos = (float)y / (float)terrainData.heightmapHeight;

                // This converts our chunk position to a latitude/longitude,
                // which we can then use to get UV coordinates from the heightmap
                // FIXME: I think this is doing a pincushion effect
                //    Someone smarter than me will have to figure this out.
                Quaternion pointRotation = ChunkRotation *
                    Quaternion.Euler( 
                        xPos * DegreesPerChunk - halfDegreesPerChunk,
                        yPos * DegreesPerChunk - halfDegreesPerChunk,
                        0
                    );

                Vector2 uv = CoordHelper.RotationToUV( pointRotation );

                // Get the pixel from the heightmap image texture at the appropriate position
                Color pix = HeightMapTexture.GetPixelBilinear( uv.x, uv.y );

                // Update the heights array
                heights[x,y] = pix.grayscale / heightMapTextureScaling;
            }
        }

        // Update the terrain data based on our changed heights array
        terrainData.SetHeights(0, 0, heights);

    }

    void BuildSplats(TerrainData terrainData)
    {
        SplatPrototype[] splatPrototypes = new SplatPrototype[ Splats.Length ];

        for (int i = 0; i < Splats.Length; i++)
        {
            splatPrototypes[i] = new SplatPrototype();
            splatPrototypes[i].texture = Splats[i].Texture;
            splatPrototypes[i].tileSize = new Vector2(Splats[i].Size, Splats[i].Size);
        }

        terrainData.splatPrototypes = splatPrototypes;
    }

    void PaintCliffs(TerrainData terrainData)
    {
        // splatMaps is a three dimensional array in the form of:
        //     splatMaps[ x, y, splatTextureID ] = (opacity from 0..1)
        float[,,] splatMaps = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);

        // Loop through each pixel in the alpha maps
        for (int aX = 0; aX < terrainData.alphamapWidth; aX++)
        {
            for (int aY = 0; aY < terrainData.alphamapHeight; aY++)
            {
                // Normal to 0..1
                float x = (float)aX / terrainData.alphamapWidth;
                float y = (float)aY / terrainData.alphamapHeight;

                // Find the steepness of the terrain at this point
                float angle = terrainData.GetSteepness( y, x ); // NOTE: x and y are flipped

                // 0 is "flat ground", 1 is "cliff ground"
                float cliffiness = angle / 90.0f;

                splatMaps[aX, aY, 0] = 1 - cliffiness; // Ground texture is inverse of cliffiness
                splatMaps[aX, aY, 1] =     cliffiness; // Cliff texture is cliffiness
            }
        }

        // Update the terrain texture maps
        terrainData.SetAlphamaps(0, 0, splatMaps);
    }

    public void SetNeighbors( DynamicTerrainChunk left, DynamicTerrainChunk top, DynamicTerrainChunk right, DynamicTerrainChunk bottom )
    {
        // TODO: Fix the seams between chunks




        Terrain t = GetComponent<Terrain>();

        Terrain leftTerrain = left == null ? null : left.terrain;
        Terrain topTerrain = top == null ? null : top.terrain;
        Terrain rightTerrain = right == null ? null : right.terrain;
        Terrain bottomTerrain = bottom == null ? null : bottom.terrain;

        // Hint to the terrain engine about how to improve LOD calculations
        t.SetNeighbors( leftTerrain, topTerrain, rightTerrain, bottomTerrain );
        t.Flush();
    }

    void BuildStructures()
    {
        // Loop through each pixel of the structures map
        // if we find pixels that aren't transparent (or whatever our criteria is)
        // then we will spawn a structure based on the color code.

        Color32[] pixels = StructureMapTexture.GetPixels32();

        Color32 c32 = new Color32(255, 0, 0, 255);


        for (int x = 0; x < StructureMapTexture.width; x++)
        {
            for (int y = 0; y < StructureMapTexture.height; y++)
            {
                Color32 p = pixels[x + y * StructureMapTexture.width];
                if( p.a < 128 )
                {
                    // transparent pixel, ignore.
                    continue;
                }

                Debug.Log("Not transparent!: " + p.ToString());

                foreach(StructureColor sc in StructureColors)
                {
                    if(sc.Color.r == p.r && sc.Color.g == p.g && sc.Color.b == p.b)
                    {
                        Debug.Log("Color match!");
                        // What is the position of the building?
                        Quaternion quaLatLon = CoordHelper.UVToRotation( new Vector2( (float)x/StructureMapTexture.width, (float)y/StructureMapTexture.height ) );

                        // Are we within DegreesPerChunk/2 of the center of the chunk,
                        // along both direction?

                        float xDiff = quaLatLon.eulerAngles.x - ChunkRotation.eulerAngles.x;
                        if(xDiff > 180)
                        {
                            xDiff = xDiff - 360;
                        }
                        float yDiff = quaLatLon.eulerAngles.y - ChunkRotation.eulerAngles.y;
                        if(yDiff > 180)
                        {
                            yDiff = yDiff - 360;
                        }

                        if( Mathf.Abs(xDiff) > DegreesPerChunk/2f || Mathf.Abs(yDiff) > DegreesPerChunk/2f )
                        {
                            // Not in our chunk!
                            continue;
                        }

                        // Spawn the correct building.
                        Vector3 pos = Vector3.zero;
                        Quaternion rot = Quaternion.identity;
                        GameObject theStructure = (GameObject)Instantiate(sc.StructurePrefab, pos, rot, this.transform);

                        // Stop looping through structure colors
                        break; // foreach
                    }
                }
            }
                
        }
    }

}
