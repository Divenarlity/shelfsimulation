using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ShelfInferenceSections {
    public int SOL, ORTA, SAĞ;
}

[Serializable]
public class ShelfInferenceDetection {
    public float confidence;
    public string section, assigned_shelf_id, region_id;
    public float[] global_bbox_xyxy;
}

[Serializable]
public class ShelfInferenceCounts {
    public int visible_candidates, segmented_shelves, matched_shelves, unknown_shelves;
    public int roi_inferences, raw_empty_predictions, mask_rejected;
    public int duplicates_removed, final_empty_spaces;
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
    public string shelf_id, status, empty_roi_mode;
    public float segmentation_confidence, mapping_confidence;
    public int empty_space_count;
    public ShelfInferenceSections sections;
    public ShelfInferenceDetection[] detections;
}

[Serializable]
public class ShelfInferenceResponse {
    public bool success;
    public string frame_id, strategy, empty_roi_mode;
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
            response.counts == null || response.shelves == null)
            throw new FormatException("Inference response is missing success, frame_id, counts, or shelves.");
        foreach (ShelfInferenceShelf shelf in response.shelves) {
            if (shelf == null || string.IsNullOrWhiteSpace(shelf.shelf_id) ||
                string.IsNullOrWhiteSpace(shelf.status) || shelf.sections == null)
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
