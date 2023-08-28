using System.Collections.Generic;
using Godot;
using LibTessDotNet;

namespace ProcSkeleton3D;

public partial class Character2D3D : CharacterBody3D
{

	private Skeleton2D skeleton2d;
	private Skeleton3D skeleton3d;


	public override void _Ready()
	{
		Init();
	}

	/// <summary>
	/// use the 2d polygon and skeleton to create a 3d mesh and skeleton,
	/// so that the 2d animation can be used in a 3d scene without resorting to viewports.
	///
	/// To do this, we read all the data from the Polygon2d and use that to create a 3d mesh
	/// We scale and offset it (baked in constants in this example)
	///
	/// We also copy the skeleton2D into a skeleton3D and (re)apply it to the mesh and create a skin.
	/// 
	/// </summary>
	private void Init()
	{
		var polygon = GetNode<Polygon2D>("%polygon");
		skeleton2d = GetNode<Skeleton2D>("%2d_skeleton");
		var mesh = GetNode<MeshInstance3D>("%mesh");
		skeleton3d = GetNode<Skeleton3D>("%3d_skeleton");
        
		
		int boneCount = skeleton2d.GetBoneCount();
		
		//unset skeleton so we can modify it? seems to have trouble if we don't do this...
		var skNodePath = mesh.Skeleton;
		mesh.Skeleton = null;
		
		//copy bones to skeleton3D
		{
			for (int i = 0; i < boneCount; i++)
			{
				var bone2D = skeleton2d.GetBone(i);
				
				skeleton3d.AddBone(bone2D.Name);
				
				//parent bone correctly
				if (bone2D.GetParent() is Bone2D boneParent)
				{
					for (int j = 0; j < boneCount; j++)
					{
						if (boneParent == skeleton2d.GetBone(j))
						{
							skeleton3d.SetBoneParent(i, j);
						}
					}
				}

				//set bone rest transform
				var skeletonRest = bone2D.GetSkeletonRest();
				skeleton3d.SetBoneRest(i, ConvertTransformTo3D(skeletonRest));

			}
			
			//reset all bone poses to rest
			//skeleton3d.LocalizeRests();
			skeleton3d.ResetBonePoses();
		}
		
		//now set the skeleton to see if that helps:
		
		mesh.Skeleton = skNodePath;
		
		//create mesh from polygon
		{
			
			//extract bone weights, we will need so we can build ContourVertex list to tessellate
			var boneWeightsByBone = new List<float[]>(boneCount);
			for (int i = 0; i < boneCount; i++)
			{
				float[] boneWeights = polygon.GetBoneWeights(i);
				boneWeightsByBone.Add(boneWeights);
			}
			
			//first we need to tessellate the polygons into triangles
			var tess = new Tess();
			
			
			if (polygon.Polygons == null || polygon.Polygons.Count == 0)
			{
				int length = polygon.Polygon.Length;
				var contour = new ContourVertex[length];
				for (var i = 0; i < length ; i++)
				{
					//make contourVertex!
					contour[i] = MakeContourVertex(polygon, i, boneWeightsByBone);
				}
				tess.AddContour(contour);
			}
			else
			{
				foreach (var poly in polygon.Polygons)
				{
					int[] verts = poly.AsInt32Array();
					int length = verts.Length;
					var contour = new ContourVertex[length];
					int ci = 0;
					foreach (int vert in verts)
					{
						contour[ci] = MakeContourVertex(polygon, vert, boneWeightsByBone);
						ci++;
					}
					tess.AddContour(contour);
				}
			}
			
			//perform the tessellation using libTessDotNet to get triangles
			tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3, VertexCombine);
			
			//now that we have triangles, use them to build the 3D mesh:
		
			var st = new SurfaceTool();
			
			st.Begin(Mesh.PrimitiveType.Triangles);
			foreach (var contourVertex in tess.Vertices)
			{
				//apply UV, position, bones, and bone weights!
				var data = (PolygonTessellationData)contourVertex.Data;
				st.SetUV(data.UV);
				st.SetBones(data.Bones);
				st.SetWeights(data.BoneWeights);
				var p = contourVertex.Position;
				st.AddVertex(new Vector3(p.X, p.Y, p.Z));
			}
			
			foreach (int element in tess.Elements)
			{
				st.AddIndex(element);
			}
			
			st.GenerateNormals();
			var arrayMesh = new ArrayMesh();
			st.Commit(arrayMesh);
			st.Clear();
			
			//TODO assign correct texture to material
			var mat = GD.Load<Material>("res://mat/figure.tres");
			arrayMesh.SurfaceSetMaterial(0, mat);

			mesh.Mesh = arrayMesh;
		}
		
		//finally, create the skin:
		mesh.Skin = skeleton3d.CreateSkinFromRestTransforms();
		
		
		//last, remove the polygon from the tree since we don't need it anymore:
		RemoveChild(polygon);
		polygon.QueueFree();
		
		return;
		
		
		// local function! vertex combine callback for tessellation!
		object VertexCombine(Vec3 p, object[] data, float[] weights)
		{
			var combined = new PolygonTessellationData();


			float[] mergedBoneWeights = new float[boneCount];
			
			
			for (var i = 0; i < data.Length; i++)
			{
				var d = (PolygonTessellationData)data[i];
				combined.UV += d.UV * weights[i];
				
				for (var j = 0; j < d.BoneWeights.Length; j++)
				{
					if (d.BoneWeights[j] > 0)
					{
						mergedBoneWeights[d.Bones[j]] += d.BoneWeights[j] * weights[i];
					}
				}
			}

			//find best bone weights -- note, we can only store 4,
			//but the source data might not respect that limitation,
			//       particularly when merging vertices, 
			//so we just store the best 4. This is a huge pain.
		
			//how many bones we've picked. stops counting at 4.
			int numBonesPicked = 0;
			//the lowest weight we've picked so far:
			float lowestPickedWeight = 0f;
		
			//picked bone indices
			int[] boneIndices = new int[4];
			//picked bone weights
			float[] boneWeights = new float[4];
		
			for (var boneIdx = 0; boneIdx < boneCount; boneIdx++)
			{
				float boneWeight = mergedBoneWeights[boneIdx];
				(lowestPickedWeight, numBonesPicked) = 
					Pick4BonesHelper(boneIdx, boneWeight, boneIndices, boneWeights, lowestPickedWeight, numBonesPicked);
			}

			combined.Bones = boneIndices;
			combined.BoneWeights = boneWeights;

			return combined;
		}

	}


	public override void _Process(double delta)
	{
		
		//copy/adapt bone transforms from skeleton2d to skeleton3d
		int boneCount = skeleton2d.GetBoneCount();
		for (int i = 0; i < boneCount; i++)
		{
			var bone2D = skeleton2d.GetBone(i);
			var boneTransform = bone2D.GetRelativeTransformToParent(skeleton2d);
			var converted = ConvertTransformTo3D(boneTransform);
			skeleton3d.SetBoneGlobalPoseOverride(i, converted, 1f, true);
		}
		
	}
	
	


	/// <summary>
	/// given an index into the polygon vertex list, create a contour vertex.
	/// to do this we need to find the top (up to) 4 bones and bone weights. bleh.
	/// </summary>
	/// <param name="polygon"></param>
	/// <param name="polyIdx"></param>
	/// <param name="boneWeightsByBone"></param>
	/// <returns></returns>
	private ContourVertex MakeContourVertex(Polygon2D polygon, int polyIdx, List<float[]> boneWeightsByBone)
	{
		var scale = 1f / 512f;
		var yOffset = 1f;
		var xOffset = -.5f;
		
		var vertex = new ContourVertex();
		var p = polygon.Polygon[polyIdx];
		vertex.Position.X = p.X * scale + xOffset;
		vertex.Position.Y = p.Y * -scale + yOffset;
			
			
		//find best bone weights -- note, we can only store 4,
		//but the source data might not respect that limitation,
		//so we just store the best 4. This is a huge pain.
		
		//how many bones we've picked. stops counting at 4.
		int numBonesPicked = 0;
		//the lowest weight we've picked so far:
		float lowestPickedWeight = 0f;
		
		//picked bone indices
		int[] boneIndices = new int[4];
		//picked bone weights
		float[] boneWeights = new float[4];
		
		for (var boneIdx = 0; boneIdx < boneWeightsByBone.Count; boneIdx++)
		{
			float[] weights = boneWeightsByBone[boneIdx];
			float boneWeight = weights[polyIdx];
			(lowestPickedWeight, numBonesPicked) = 
				Pick4BonesHelper(boneIdx, boneWeight, boneIndices, boneWeights, lowestPickedWeight, numBonesPicked);
		}
			
		vertex.Data = new PolygonTessellationData
		{
			UV = polygon.UV[polyIdx] * scale,
			Bones = boneIndices,
			BoneWeights = boneWeights

		};

		return vertex;
	}

	/// <summary>
	/// helper func to pick the best 4 bones for this vertex
	/// </summary>
	/// <param name="boneIdx"></param>
	/// <param name="boneWeight"></param>
	/// <param name="boneIndices"></param>
	/// <param name="boneWeights"></param>
	/// <param name="lowestPickedWeight"></param>
	/// <param name="numBonesPicked"></param>
	/// <returns></returns>
	private static (float lowestPickedWeight, int numBonesPicked) Pick4BonesHelper
		(int boneIdx, float boneWeight, int[] boneIndices, float[] boneWeights, float lowestPickedWeight, int numBonesPicked)
	{
		if (boneWeight > lowestPickedWeight)
		{
			//need to pick it:
			if (numBonesPicked < 4)
			{
				boneIndices[numBonesPicked] = boneIdx;
				boneWeights[numBonesPicked] = boneWeight;
				numBonesPicked++;
				if (numBonesPicked == 4)
				{
					lowestPickedWeight = float.PositiveInfinity;
					foreach (float weight in boneWeights)
					{
						lowestPickedWeight = Mathf.Min(lowestPickedWeight, weight);
					}
				}
			}
			else
			{
				//already picked 4 bones, need to replace lowest:
				var lowestOldPick = float.PositiveInfinity;
				int lowestPickedIndex = 0;
				for (var i = 0; i < boneWeights.Length; i++)
				{
					float oldWeight = boneWeights[i];
					if (oldWeight < lowestOldPick)
					{
						lowestOldPick = oldWeight;
						lowestPickedIndex = i;
					}
				}

				boneIndices[lowestPickedIndex] = boneIdx;
				boneWeights[lowestPickedIndex] = boneWeight;
				lowestPickedWeight = Mathf.Min(boneWeight, lowestOldPick);
			}
		}

		return (lowestPickedWeight, numBonesPicked);
	}

	/// <summary>
	/// convert a pixel-space Transform2D into a world-space Transform3D
	///
	/// </summary>
	/// <param name="t2"></param>
	/// <returns></returns>
	private Transform3D ConvertTransformTo3D(Transform2D t2)
	{
		var scale = 1f / 512f;
        
		var t2X = t2.X;
		var t2Y = t2.Y;
		var t2O = t2.Origin * scale;
		t2O.Y *= -1;

		var t3X = new Vector3(t2X.X, t2X.Y, 0);
		var t3Y = new Vector3(t2Y.X, t2Y.Y, 0);
		var t3O = new Vector3(t2O.X, t2O.Y, 0);

		var t3 = new Transform3D(t3X, t3Y, new Vector3(0, 0, 1), t3O);

		return t3;
	}

	/// <summary>
	/// data obj to hold uv and bone data for tessellation step
	/// </summary>
	private struct PolygonTessellationData
	{
		
		public Vector2 UV = default;
		public int[] Bones = null;
		public float[] BoneWeights = null;

		public PolygonTessellationData()
		{
		}
	}
}