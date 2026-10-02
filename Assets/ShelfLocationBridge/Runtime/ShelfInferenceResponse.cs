using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ShelfInferenceSections {
    public int SOL, ORTA, SAĞ;
}

[Serializable]
public class ShelfInferenceDetection {
    public int assigned_shelf_index, class_id;
    public float confidence;
    public string section, assigned_shelf_id, parent_shelf_id, shelf_level_id;
    public string region_id, class_name, inference_source;
    public float[] global_bbox_xyxy;
}

[Serializable]
public class ShelfPolygonPoint {
    public float x, y;
}

[Serializable]
public class ShelfInferenceCounts {
    public int visible_candidates, segmented_shelves, detected_shelves, matched_shelves, unknown_shelves;
    public int matched_parent_regions;
    public int roi_inferences, raw_empty_predictions, mask_rejected;
    public int duplicates_removed, final_empty_spaces;
    public int shelf_rois_selected, shelf_rois_limited;
    public int empty_inference_calls, empty_model_predict_calls, empty_inference_inputs;
}

[Serializable]
public class ShelfInferenceTiming {
    public float shelf_inference_ms, empty_batch_inference_ms, parent_association_ms;
    public float empty_association_ms, association_ms;
    public float visualization_render_ms, total_pipeline_ms, visualization_encode_ms;
}

[Serializable]
public class ShelfInferenceDebug {
    public bool debug_only;
    public string directory, failure_stage;
    public int full_frame_raw, full_shelf_roi_raw, tile_raw, tile_production_raw;
    public int production_raw, mask_rejected, final;
}

[Serializable]
public class ShelfInferenceShelf {
    public string shelf_id, parent_shelf_id, shelf_level_id;
    public string status, empty_roi_mode, model_task, empty_inference_source;
    public float confidence, segmentation_confidence, mapping_confidence;
    public int shelf_index, level_number, empty_space_count;
    public bool empty_inference_selected;
    public float[] bbox_xyxy, global_bbox_xyxy, roi_bbox_xyxy;
    public ShelfPolygonPoint[] mask_polygon;
    public ShelfInferenceSections sections;
    public ShelfInferenceDetection[] detections;
}

[Serializable]
public class ShelfInferenceResponse {
    public bool success;
    public string frame_id, strategy, empty_inference_mode, empty_roi_mode, visualization_url;
    public ImageMetadata image;
    public ShelfInferenceCounts counts;
    public ShelfInferenceTiming timing;
    public ShelfInferenceShelf[] shelves;
    public ShelfInferenceDebug debug;

    public static ShelfInferenceResponse Parse(string json) {
        if (string.IsNullOrWhiteSpace(json))
            throw new FormatException("Inference response is empty.");
        ShelfInferenceResponse response;
        try {
            response = JsonUtility.FromJson<ShelfInferenceResponse>(json);
        } catch (Exception exception) {
            throw new FormatException("Inference response is not valid JSON.", exception);
        }
        Validate(response);
        return response;
    }

    public static void Validate(ShelfInferenceResponse response) {
        if (response == null || !response.success || string.IsNullOrWhiteSpace(response.frame_id) ||
            response.image == null || response.image.width <= 0 || response.image.height <= 0 ||
            response.counts == null || response.shelves == null)
            throw new FormatException(
                "Inference response is missing success, frame_id, image, counts, or shelves.");
        foreach (ShelfInferenceShelf shelf in response.shelves) {
            if (shelf == null || string.IsNullOrWhiteSpace(shelf.shelf_id) ||
                string.IsNullOrWhiteSpace(shelf.status) || shelf.sections == null ||
                shelf.global_bbox_xyxy == null || shelf.global_bbox_xyxy.Length != 4 ||
                shelf.detections == null)
                throw new FormatException("Inference response contains an incomplete shelf.");
        }
    }
}

[Serializable]
public sealed class DualShelfInferenceTiming {
    public float dual_total_ms, shelf_batch_inference_ms;
    public int shelf_batch_size;
    public float left_mapping_ms, right_mapping_ms, empty_batch_inference_ms;
    public int empty_batch_size;
    public float left_association_ms, right_association_ms;
    public float left_visualization_render_ms, right_visualization_render_ms;
    public float visualization_encode_ms;
}

[Serializable]
public sealed class DualShelfInferenceCounts {
    public int shelf_model_predict_calls, shelf_model_inputs, empty_model_predict_calls;
    public int left_empty_inputs, right_empty_inputs, total_empty_inputs;
    public int left_final_empty_spaces, right_final_empty_spaces;
}

[Serializable]
public sealed class ScanPersistenceResult {
    public bool enabled, saved, duplicate_ignored;
    public string session_id, station_id, session_directory, warning;
    public float save_ms;
}

[Serializable]
public sealed class DualShelfInferenceResponse {
    public bool success;
    public string mode;
    public ShelfInferenceResponse left, right;
    public DualShelfInferenceTiming timing;
    public DualShelfInferenceCounts counts;
    public ScanPersistenceResult persistence;

    public static DualShelfInferenceResponse Parse(string json) {
        if (string.IsNullOrWhiteSpace(json))
            throw new FormatException("Dual inference response is empty.");
        DualShelfInferenceResponse response;
        try {
            response = JsonUtility.FromJson<DualShelfInferenceResponse>(json);
        } catch (Exception exception) {
            throw new FormatException("Dual inference response is not valid JSON.", exception);
        }
        if (response == null || !response.success || response.mode != "dual_camera_batch" ||
            response.timing == null || response.counts == null)
            throw new FormatException("Dual inference response is missing its batch contract.");
        ShelfInferenceResponse.Validate(response.left);
        ShelfInferenceResponse.Validate(response.right);
        if (response.left.frame_id == response.right.frame_id)
            throw new FormatException("Dual inference response frame IDs must be distinct.");
        if (response.counts.shelf_model_predict_calls != 1 ||
            response.counts.shelf_model_inputs != 2 ||
            response.counts.empty_model_predict_calls < 0 ||
            response.counts.empty_model_predict_calls > 1 ||
            response.counts.total_empty_inputs !=
                response.counts.left_empty_inputs + response.counts.right_empty_inputs)
            throw new FormatException("Dual inference response contains invalid batch counts.");
        return response;
    }
}

public class ShelfInferenceState {
    readonly Dictionary<string, string> previous = new();

    public bool Update(ShelfInferenceShelf shelf) {
        if (shelf == null || string.IsNullOrWhiteSpace(shelf.shelf_id) ||
            shelf.shelf_id == "UNKNOWN_SHELF") return false;
        ShelfInferenceSections sections = shelf.sections ?? new ShelfInferenceSections();
        string value =
            $"{shelf.status}|{shelf.empty_space_count}|{sections.SOL}|{sections.ORTA}|{sections.SAĞ}";
        if (previous.TryGetValue(shelf.shelf_id, out string oldValue) && oldValue == value)
            return false;
        previous[shelf.shelf_id] = value;
        return true;
    }
}
