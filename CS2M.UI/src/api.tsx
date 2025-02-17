import {trigger} from "cs2/api";
import mod from "../mod.json";

export function setVal(name: string, value: any) {
    trigger(mod.id, name, value);
}