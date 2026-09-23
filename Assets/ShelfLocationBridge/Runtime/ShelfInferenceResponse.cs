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
    public string section, assigned_shelf_id, region_id, class_name, inference_source;
    public float[] global_bbox_xyxy;
}

[Serializable]
public class ShelfInferenceCounts {
    public int visible_candidates, segmented_shelves, detected_shelves, matched_shelves, unknown_shelves;
    public int roi_inferences, raw_empty_predictions, mask_rejected;
    public int duplicates_removed, final_empty_spaces;
    public int shelf_rois_selected, shelf_rois_limited;
    public int empty_inference_calls, empty_model_predict_calls, empty_inference_inputs;
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
    public string shelf_id, status, empty_roi_mode, model_task, empty_inference_source;
    public float confidence, segmentation_confidence, mapping_confidence;
    public int shelf_index, empty_space_count;
    public bool empty_inference_selected;
    public float[] bbox_xyxy, global_bbox_xyxy, roi_bbox_xyxy;
    public ShelfInferenceSections sections;
    public ShelfInferenceDetection[] detections;
}

[Serializable]
public class ShelfInferenceResponse {
    public bool success;
    public string frame_id, strategy, empty_inference_mode, empty_roi_mode;
    public ImageMetadata image;
    public ShelfInferenceCounts counts;
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
